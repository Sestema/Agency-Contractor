using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Win11DesktopApp.Converters
{
    public class ImagePathConverter : IValueConverter
    {
        private const int MaxCacheEntries = 60;

        private static readonly ConcurrentDictionary<string, (BitmapSource image, DateTime lastWrite, DateTime lastAccessed)> _cache = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var now = DateTime.UtcNow;

                if (_cache.TryGetValue(path, out var cached) && cached.lastWrite == lastWrite)
                {
                    _cache[path] = (cached.image, cached.lastWrite, now);
                    return cached.image;
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var stream = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        var decoder = BitmapDecoder.Create(stream,
                            BitmapCreateOptions.IgnoreImageCache,
                            BitmapCacheOption.OnLoad);
                        var frame = decoder.Frames[0];
                        frame.Freeze();

                        _cache[path] = (frame, lastWrite, now);
                        EvictIfOverCapacity();
                        return frame;
                    }
                    catch when (attempt < 2)
                    {
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        public static void InvalidateCache(string? path = null)
        {
            if (path != null)
                _cache.TryRemove(path, out _);
            else
                _cache.Clear();
        }

        /// <summary>
        /// Removes cached previews whose files live under <paramref name="employeeFolder"/>.
        /// Uses a trailing directory separator so a folder is not treated as a prefix of another name.
        /// </summary>
        public static void InvalidateCacheForFolder(string? employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return;

            if (!TryGetFolderPrefix(employeeFolder, out var prefix))
                return;

            foreach (var key in _cache.Keys)
            {
                if (!TryGetFullPath(key, out var fullKey))
                    continue;

                if (fullKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    _cache.TryRemove(key, out _);
            }
        }

        private static void EvictIfOverCapacity()
        {
            var excess = _cache.Count - MaxCacheEntries;
            if (excess <= 0)
                return;

            while (excess-- > 0 && _cache.Count > MaxCacheEntries)
            {
                string? oldestKey = null;
                var oldestAccess = DateTime.MaxValue;
                foreach (var pair in _cache)
                {
                    if (pair.Value.lastAccessed < oldestAccess)
                    {
                        oldestAccess = pair.Value.lastAccessed;
                        oldestKey = pair.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                _cache.TryRemove(oldestKey, out _);
            }
        }

        private static bool TryGetFolderPrefix(string folder, out string prefix)
        {
            prefix = string.Empty;
            if (!TryGetFullPath(folder, out var full))
                return false;

            prefix = full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
                ? full
                : full + Path.DirectorySeparatorChar;
            return true;
        }

        private static bool TryGetFullPath(string path, out string fullPath)
        {
            fullPath = string.Empty;
            try
            {
                fullPath = Path.GetFullPath(path);
                return !string.IsNullOrEmpty(fullPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
