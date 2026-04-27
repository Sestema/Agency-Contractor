using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using QRCoder;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Telegram
{
    public sealed class TelegramPairingService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly ConcurrentDictionary<string, DateTime> _codes = new(StringComparer.OrdinalIgnoreCase);

        public TelegramPairingService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService;
        }

        public string GenerateCode(TimeSpan? lifetime = null)
        {
            var code = $"PAIR_{Guid.NewGuid():N}".ToUpperInvariant()[..13];
            _codes[code] = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10));
            CleanupExpiredCodes();
            return code;
        }

        public DateTime GetExpiryUtc(string code)
        {
            return _codes.TryGetValue(code, out var expiresAt)
                ? expiresAt
                : DateTime.UtcNow;
        }

        public string BuildDeepLink(string botUsername, string code)
        {
            var username = (botUsername ?? string.Empty).Trim().TrimStart('@');
            return $"https://t.me/{username}?start={code}";
        }

        public BitmapImage BuildQrImage(string deepLink)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(deepLink, QRCodeGenerator.ECCLevel.Q);
            var qr = new PngByteQRCode(data);
            var bytes = qr.GetGraphic(20);

            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public bool TryConsume(string code, long telegramUserId, string displayName, string username)
        {
            CleanupExpiredCodes();

            if (!_codes.TryRemove(code, out var expiresAt) || expiresAt < DateTime.UtcNow)
                return false;

            var settings = _appSettingsService.Settings.Telegram;
            var existing = settings.AuthorizedUsers.FirstOrDefault(u => u.TelegramUserId == telegramUserId);
            var now = DateTime.UtcNow.ToString("O");

            if (existing == null)
            {
                settings.AuthorizedUsers.Add(new TelegramAuthorizedUser
                {
                    TelegramUserId = telegramUserId,
                    DisplayName = displayName ?? string.Empty,
                    Username = username ?? string.Empty,
                    LinkedAtUtc = now,
                    LastActiveAtUtc = now
                });
            }
            else
            {
                existing.DisplayName = displayName ?? existing.DisplayName;
                existing.Username = username ?? existing.Username;
                existing.LastActiveAtUtc = now;
            }

            _appSettingsService.SaveSettings();
            return true;
        }

        private void CleanupExpiredCodes()
        {
            var now = DateTime.UtcNow;
            foreach (var item in _codes.Where(kvp => kvp.Value < now).ToArray())
                _codes.TryRemove(item.Key, out _);
        }
    }
}
