using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using QRCoder;
using Win11DesktopApp.Invoices.Models;

namespace Win11DesktopApp.Invoices.Services;

public sealed class InvoiceQrPaymentPreview
{
    public bool IsReady { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public BitmapImage? Image { get; init; }
    public byte[]? PngBytes { get; init; }
}

public sealed class InvoicePaymentDetailsPreview
{
    public string BankName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string Iban { get; init; } = string.Empty;
    public string Swift { get; init; } = string.Empty;
    public string VariableSymbol { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public bool UsesCalculatedIban { get; init; }
    public bool HasAnyData =>
        !string.IsNullOrWhiteSpace(BankName)
        || !string.IsNullOrWhiteSpace(AccountNumber)
        || !string.IsNullOrWhiteSpace(BankCode)
        || !string.IsNullOrWhiteSpace(Iban)
        || !string.IsNullOrWhiteSpace(Swift)
        || !string.IsNullOrWhiteSpace(VariableSymbol)
        || !string.IsNullOrWhiteSpace(InvoiceNumber);
}

public sealed class InvoiceQrPaymentService
{
    public InvoicePaymentDetailsPreview DescribePayment(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var normalizedIban = NormalizeIban(document.Supplier.BankIban);
        var resolvedIban = ResolvePaymentAccount(normalizedIban, document.Supplier.LegacyAccountNumber);
        var normalizedAccount = NormalizeLegacyAccountNumber(document.Supplier.LegacyAccountNumber);
        var bankCode = ExtractBankCode(normalizedAccount, resolvedIban);

        return new InvoicePaymentDetailsPreview
        {
            BankName = (document.Supplier.BankName ?? string.Empty).Trim(),
            AccountNumber = normalizedAccount,
            BankCode = bankCode,
            Iban = resolvedIban,
            Swift = NormalizeBic(document.Supplier.BankSwift),
            VariableSymbol = NormalizeDigits(document.VariableSymbol),
            InvoiceNumber = (document.Number ?? string.Empty).Trim(),
            UsesCalculatedIban = string.IsNullOrWhiteSpace(normalizedIban) && !string.IsNullOrWhiteSpace(resolvedIban)
        };
    }

    public InvoiceQrPaymentPreview CreatePreview(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.ShowQrCode)
        {
            return new InvoiceQrPaymentPreview
            {
                IsReady = false,
                Message = string.Empty
            };
        }

        if (!string.Equals(document.PaymentMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase))
        {
            return new InvoiceQrPaymentPreview
            {
                Message = Res("InvoicesQrRequiresTransfer")
            };
        }

        var paymentDetails = DescribePayment(document);
        if (string.IsNullOrWhiteSpace(paymentDetails.Iban))
        {
            return new InvoiceQrPaymentPreview
            {
                Message = Res("InvoicesQrNeedsIban")
            };
        }

        if (document.AmountToPay <= 0m)
        {
            return new InvoiceQrPaymentPreview
            {
                Message = Res("InvoicesQrNeedsPositiveAmount")
            };
        }

        var payloadResult = BuildPayload(document, paymentDetails.Iban);
        if (!payloadResult.IsReady)
            return payloadResult;

        var pngBytes = CreateQrPngBytes(payloadResult.Payload);
        return new InvoiceQrPaymentPreview
        {
            IsReady = true,
            Message = string.Format(Res("InvoicesQrReady"), document.QrPaymentFormat.ToUpperInvariant()),
            Payload = payloadResult.Payload,
            PngBytes = pngBytes,
            Image = CreateQrImage(pngBytes)
        };
    }

    private InvoiceQrPaymentPreview BuildPayload(InvoiceDocument document, string iban)
    {
        return document.QrPaymentFormat.ToLowerInvariant() switch
        {
            "epc" => BuildEpcPayload(document, iban),
            _ => BuildSpaydPayload(document, iban)
        };
    }

    private static InvoiceQrPaymentPreview BuildSpaydPayload(InvoiceDocument document, string iban)
    {
        var parts = new List<string>
        {
            "SPD",
            "1.0",
            $"ACC:{iban}",
            $"AM:{document.AmountToPay.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"CC:{document.Currency.ToUpperInvariant()}"
        };

        var variableSymbol = NormalizeDigits(document.VariableSymbol);
        if (!string.IsNullOrWhiteSpace(variableSymbol))
            parts.Add($"X-VS:{variableSymbol}");

        var constantSymbol = NormalizeDigits(document.ConstantSymbol);
        if (!string.IsNullOrWhiteSpace(constantSymbol))
            parts.Add($"X-KS:{constantSymbol}");

        if (document.DueDate != default)
            parts.Add($"DT:{document.DueDate:yyyyMMdd}");

        var message = SanitizeMessage(!string.IsNullOrWhiteSpace(document.Number)
            ? $"Invoice {document.Number}"
            : document.NotesBelow);
        if (!string.IsNullOrWhiteSpace(message))
            parts.Add($"MSG:{message}");

        return new InvoiceQrPaymentPreview
        {
            IsReady = true,
            Payload = string.Join("*", parts)
        };
    }

    private static InvoiceQrPaymentPreview BuildEpcPayload(InvoiceDocument document, string iban)
    {
        if (!string.Equals(document.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            return new InvoiceQrPaymentPreview
            {
                Message = Res("InvoicesQrRequiresEuro")
            };
        }

        var receiver = SanitizeMessage(document.Supplier.Name, 70);
        if (string.IsNullOrWhiteSpace(receiver))
        {
            return new InvoiceQrPaymentPreview
            {
                Message = Res("InvoicesQrNeedsReceiverName")
            };
        }

        var bic = NormalizeBic(document.Supplier.BankSwift);
        var remittance = SanitizeMessage(!string.IsNullOrWhiteSpace(document.VariableSymbol)
            ? $"VS {NormalizeDigits(document.VariableSymbol)}"
            : document.Number, 140);

        var lines = new[]
        {
            "BCD",
            "002",
            "1",
            "SCT",
            bic,
            receiver,
            iban,
            $"EUR{document.AmountToPay.ToString("0.00", CultureInfo.InvariantCulture)}",
            string.Empty,
            string.Empty,
            remittance,
            string.Empty
        };

        return new InvoiceQrPaymentPreview
        {
            IsReady = true,
            Payload = string.Join("\n", lines)
        };
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

    private static string NormalizeIban(string? iban)
        => new string((iban ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();

    private static string NormalizeLegacyAccountNumber(string? legacyAccountNumber)
        => string.Join(" ", (legacyAccountNumber ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string ResolvePaymentAccount(string? iban, string? legacyAccountNumber)
    {
        var normalizedIban = NormalizeIban(iban);
        if (!string.IsNullOrWhiteSpace(normalizedIban))
            return normalizedIban;

        return TryConvertCzechAccountToIban(legacyAccountNumber) ?? string.Empty;
    }

    private static string? TryConvertCzechAccountToIban(string? legacyAccountNumber)
    {
        if (string.IsNullOrWhiteSpace(legacyAccountNumber))
            return null;

        var raw = legacyAccountNumber.Trim();
        var slashIndex = raw.LastIndexOf('/');
        if (slashIndex <= 0 || slashIndex >= raw.Length - 1)
            return null;

        var accountPart = raw[..slashIndex];
        var bankCodeDigits = NormalizeDigits(raw[(slashIndex + 1)..]);
        if (bankCodeDigits.Length != 4)
            return null;

        var accountSegments = accountPart.Split('-', 2, StringSplitOptions.TrimEntries);
        var prefixDigits = accountSegments.Length == 2 ? NormalizeDigits(accountSegments[0]) : string.Empty;
        var accountDigits = NormalizeDigits(accountSegments.Length == 2 ? accountSegments[1] : accountSegments[0]);
        if (accountDigits.Length == 0 || accountDigits.Length > 10 || prefixDigits.Length > 6)
            return null;

        var bban = string.Concat(
            bankCodeDigits,
            prefixDigits.PadLeft(6, '0'),
            accountDigits.PadLeft(10, '0'));

        var checksum = CalculateIbanChecksum(bban + "123500");
        if (checksum <= 0)
            return null;

        return $"CZ{checksum:00}{bban}";
    }

    private static int CalculateIbanChecksum(string value)
    {
        var remainder = 0;
        foreach (var ch in value)
        {
            if (!char.IsDigit(ch))
                return 0;

            remainder = ((remainder * 10) + (ch - '0')) % 97;
        }

        return 98 - remainder;
    }

    private static string NormalizeDigits(string? value)
        => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string ExtractBankCode(string? legacyAccountNumber, string? iban)
    {
        var rawAccount = legacyAccountNumber ?? string.Empty;
        var slashIndex = rawAccount.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < rawAccount.Length - 1)
        {
            var bankCodeFromAccount = NormalizeDigits(rawAccount[(slashIndex + 1)..]);
            if (bankCodeFromAccount.Length == 4)
                return bankCodeFromAccount;
        }

        var normalizedIban = NormalizeIban(iban);
        if (normalizedIban.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) && normalizedIban.Length >= 8)
            return normalizedIban.Substring(4, 4);

        return string.Empty;
    }

    private static string NormalizeBic(string? bic)
        => new string((bic ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();

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
        => App.Current?.TryFindResource(key) as string ?? key;
}
