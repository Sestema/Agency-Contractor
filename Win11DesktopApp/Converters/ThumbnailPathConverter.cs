using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Thumbnail variant of <see cref="ImagePathConverter"/>. Decodes the source
    /// image at the requested pixel size via <see cref="BitmapImage.DecodePixelWidth"/>
    /// so lists and tiles do not hold full-resolution bitmaps in memory.
    ///
    /// Pass the desired pixel width through <c>ConverterParameter</c>. Sensible
    /// defaults are 96 (list circle) or 256 (tile). The cache is keyed by both
    /// path and decode size, so the same file can coexist as multiple thumbnail
    /// sizes without trampling each other.
    ///
    /// IMPORTANT: This converter is intentionally separate from
    /// <see cref="ImagePathConverter"/>. Anywhere a full-resolution frame is
    /// still required (document preview, photo cropping, employee details hero
    /// image) must keep using <c>ImagePathConverter</c>.
    /// </summary>
    public class ThumbnailPathConverter : IValueConverter
    {
        private const int DefaultDecodeWidth = 128;

        private static readonly ConcurrentDictionary<(string Path, int Width), (BitmapSource image, DateTime lastWrite)> _cache = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var decodeWidth = ResolveDecodeWidth(parameter);

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var cacheKey = (path, decodeWidth);

                if (_cache.TryGetValue(cacheKey, out var cached) && cached.lastWrite == lastWrite)
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

                        // Mirror the decode pattern from ImagePathConverter (which
                        // is known to work reliably) and then downscale via
                        // TransformedBitmap to keep only a thumbnail-sized bitmap
                        // alive in the cache. Using BitmapImage + StreamSource +
                        // DecodePixelWidth was unreliable: with the stream being
                        // disposed right after EndInit, WPF could end up with a
                        // frozen but empty BitmapImage, which rendered nothing.
                        var decoder = BitmapDecoder.Create(stream,
                            BitmapCreateOptions.IgnoreImageCache,
                            BitmapCacheOption.OnLoad);
                        var frame = decoder.Frames[0];

                        BitmapSource result;
                        if (frame.PixelWidth > decodeWidth && frame.PixelWidth > 0)
                        {
                            var scale = (double)decodeWidth / frame.PixelWidth;
                            var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                            scaled.Freeze();
                            result = scaled;
                        }
                        else
                        {
                            frame.Freeze();
                            result = frame;
                        }

                        _cache[cacheKey] = (result, lastWrite);
                        return result;
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
    }
}
