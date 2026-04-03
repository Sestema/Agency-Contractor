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
        private static readonly ConcurrentDictionary<string, (BitmapSource image, DateTime lastWrite)> _cache = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);

                if (_cache.TryGetValue(path, out var cached) && cached.lastWrite == lastWrite)
                    return cached.image;

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

                        _cache[path] = (frame, lastWrite);
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
    }
}
