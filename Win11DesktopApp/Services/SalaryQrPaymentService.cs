using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Win11DesktopApp.Services
{
    public sealed class SalaryQrPaymentPreview
    {
        public bool IsReady { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public BitmapImage? Image { get; init; }
        public byte[]? PngBytes { get; init; }
        public string EmployeeName { get; init; } = string.Empty;
        public string AccountNumber { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string Iban { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string MessageText { get; init; } = string.Empty;
    }

    public static class SalaryQrPaymentService
    {
        public static SalaryQrPaymentPreview CreatePreview(
            string employeeName,
            string? accountNumber,
            string? bankName,
            decimal amount,
            string? messageText)
        {
            var name = (employeeName ?? string.Empty).Trim();
            var account = (accountNumber ?? string.Empty).Trim();
            var resolvedBank = (bankName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedBank))
                CzechBankAccountResolver.TryResolveBankName(account, out resolvedBank);

            var iban = CzechBankAccountResolver.TryConvertToIban(account) ?? string.Empty;
            var recipientName = SanitizeMessage(name, 35);
            var msg = SanitizeMessage(messageText);

            if (amount <= 0m)
            {
                return new SalaryQrPaymentPreview
                {
                    EmployeeName = name,
                    AccountNumber = account,
                    BankName = resolvedBank,
                    Iban = iban,
                    Amount = amount,
                    MessageText = msg,
                    Message = Res("FinQrNeedsAmount")
                };
            }

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(iban))
            {
                return new SalaryQrPaymentPreview
                {
                    EmployeeName = name,
                    AccountNumber = account,
                    BankName = resolvedBank,
                    Iban = iban,
                    Amount = amount,
                    MessageText = msg,
                    Message = Res("FinQrNeedsAccount")
                };
            }

            var payload = BuildSpaydPayload(iban, amount, recipientName, msg);
            var pngBytes = CreateQrPngBytes(payload);
            return new SalaryQrPaymentPreview
            {
                IsReady = true,
                Payload = payload,
                PngBytes = pngBytes,
                Image = CreateQrImage(pngBytes),
                EmployeeName = name,
                AccountNumber = account,
                BankName = resolvedBank,
                Iban = iban,
                Amount = amount,
                MessageText = msg
            };
        }

        private static string BuildSpaydPayload(string iban, decimal amount, string recipientName, string message)
        {
            var parts = new List<string>
            {
                "SPD",
                "1.0",
                $"ACC:{iban}",
                $"AM:{amount.ToString("0.00", CultureInfo.InvariantCulture)}",
                "CC:CZK"
            };

            if (!string.IsNullOrWhiteSpace(recipientName))
                parts.Add($"RN:{recipientName}");

            if (!string.IsNullOrWhiteSpace(message))
                parts.Add($"MSG:{message}");

            return string.Join("*", parts);
        }

        private static byte[]? CreateQrPngBytes(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data);
            return png.GetGraphic(10);
        }

        private static BitmapImage? CreateQrImage(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static string SanitizeMessage(string? value, int maxLength = 60)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sanitized = value
                .Replace("*", " ", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
        }

        private static string Res(string key)
            => Application.Current?.TryFindResource(key) as string ?? key;
    }
}
