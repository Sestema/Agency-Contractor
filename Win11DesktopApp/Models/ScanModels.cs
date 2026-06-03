using System;

namespace Win11DesktopApp.Models
{
    public enum ScanColorMode
    {
        Color = 0,
        Grayscale = 1,
        BlackWhite = 2
    }

    public enum ScanSource
    {
        Auto = 0,
        Flatbed = 1,
        Feeder = 2
    }

    public sealed class ScanPage
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; } = string.Empty;
        public string? ThumbnailPath { get; set; }
        public int Order { get; set; }
        public DateTime ScannedAt { get; init; } = DateTime.Now;
        public bool IsEdited { get; set; }
    }

    public sealed class ScanSettings
    {
        public int Dpi { get; set; } = 300;
        public ScanColorMode ColorMode { get; set; } = ScanColorMode.Color;
        public ScanSource Source { get; set; } = ScanSource.Auto;
        public string? DeviceId { get; set; }
        public string Provider { get; set; } = "WIA";
    }

    public sealed class ScannerDeviceInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Provider { get; init; } = "WIA";
    }

    public sealed class ScanExportOptions
    {
        public int JpegQuality { get; set; } = 90;
    }
}
