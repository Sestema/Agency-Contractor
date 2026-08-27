using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Thumbnail variant of <see cref="ImagePathConverter"/>. Decodes only the
    /// requested pixel width via <see cref="BitmapImage.DecodePixelWidth"/> so
    /// lists and tiles do not hold full-resolution bitmaps in memory.
    /// </summary>
    public class ThumbnailPathConverter : IValueConverter
    {
        private const int DefaultDecodeWidth = 128;
        private const int MaxCacheEntries = 200;

        private static readonly ConcurrentDictionary<(string Path, int Width), (BitmapSource image, DateTime lastWrite, DateTime lastAccessed)> _cache = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var decodeWidth = ResolveDecodeWidth(parameter);
            return TryLoadIntoCache(path, decodeWidth);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        public static void InvalidateCache(string? path = null)
        {
            if (path != null)
            {
                foreach (var key in _cache.Keys)
                {
                    if (string.Equals(key.Path, path, StringComparison.OrdinalIgnoreCase))
                        _cache.TryRemove(key, out _);
                }
            }
            else
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Warms the thumbnail cache on a background dispatcher priority so UI
        /// bindings hit cached bitmaps instead of decoding during layout.
        /// </summary>
        public static async Task PreloadAsync(
            IEnumerable<string?> paths,
            int decodeWidth = DefaultDecodeWidth,
            CancellationToken cancellationToken = default)
        {
            var uniquePaths = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => File.Exists(path))
                .ToList();

            if (uniquePaths.Count == 0)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            const int batchSize = 12;
            for (var index = 0; index < uniquePaths.Count; index += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = uniquePaths.Skip(index).Take(batchSize).ToList();
                await dispatcher.InvokeAsync(() =>
                {
                    foreach (var path in batch)
                        TryLoadIntoCache(path, decodeWidth);
                }, DispatcherPriority.Background);

                await Task.Yield();
            }
        }

        private static BitmapSource? TryLoadIntoCache(string path, int decodeWidth)
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var cacheKey = (path, decodeWidth);
                var now = DateTime.UtcNow;

                if (_cache.TryGetValue(cacheKey, out var cached) && cached.lastWrite == lastWrite)
                {
                    _cache[cacheKey] = (cached.image, cached.lastWrite, now);
                    return cached.image;
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                        bitmap.DecodePixelWidth = decodeWidth;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        _cache[cacheKey] = (bitmap, lastWrite, now);
                        EvictIfOverCapacity();
                        return bitmap;
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

        private static int ResolveDecodeWidth(object? parameter)
        {
            if (parameter == null)
                return DefaultDecodeWidth;

            if (parameter is int directInt && directInt > 0)
                return directInt;

            if (parameter is double directDouble && directDouble > 0)
                return (int)directDouble;

            if (parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                return parsed;

            return DefaultDecodeWidth;
        }

        private static void EvictIfOverCapacity()
        {
            var excess = _cache.Count - MaxCacheEntries;
            if (excess <= 0)
                return;

            while (excess-- > 0 && _cache.Count > MaxCacheEntries)
            {
                (string Path, int Width)? oldestKey = null;
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

                _cache.TryRemove(oldestKey.Value, out _);
            }
        }
    }
}
