using System.Net;
using System.Net.Http;
using System.Text.Json;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Invoices.Services;

public sealed class AresCompanyData
{
    public string Ico { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Dic { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "CZ";
}

public sealed class AresLookupService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly InvoiceStorageService _storageService;

    public AresLookupService(InvoiceStorageService storageService)
    {
        _storageService = storageService;
    }

    public async Task<AresCompanyData?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default)
    {
        var normalizedIco = NormalizeIco(ico);
        if (string.IsNullOrWhiteSpace(normalizedIco))
            return null;

        var cached = _storageService.ReadAresCache<AresCompanyData>(normalizedIco);
        if (cached != null)
            return cached;

        var requestUrl = $"https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{normalizedIco}";
        using var response = await Http.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var parsed = ParseResponse(json.RootElement);
        if (parsed != null)
            _storageService.WriteAresCache(normalizedIco, parsed);

        return parsed;
    }

    public void ApplyToParty(InvoiceParty target, AresCompanyData source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.Name = source.Name;
        target.Ico = source.Ico;
        target.Dic = source.Dic;
        target.VatId = source.Dic;
        target.Street = source.Street;
        target.City = source.City;
        target.PostalCode = source.PostalCode;
        target.Country = source.Country;
        target.IsVatPayer = !string.IsNullOrWhiteSpace(source.Dic);
    }

    private static AresCompanyData? ParseResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var address = root.TryGetProperty("sidlo", out var sidloElement) ? sidloElement : default;

        return new AresCompanyData
        {
            Ico = GetString(root, "ico"),
            Name = GetString(root, "obchodniJmeno"),
            Dic = GetString(root, "dic"),
            Street = BuildStreet(address),
            City = BuildCity(address),
            PostalCode = BuildPostalCode(address),
            Country = GetString(address, "kodStatu", fallback: "CZ")
        };
    }

    private static string BuildStreet(JsonElement address)
    {
        if (address.ValueKind != JsonValueKind.Object)
            return string.Empty;

        var streetName = GetString(address, "nazevUlice");
        var houseNumber = GetString(address, "cisloDomovni");
        var orientationNumber = GetString(address, "cisloOrientacni");
        var orientationLetter = GetString(address, "cisloOrientacniPismeno");
        var district = GetString(address, "nazevCastiObce");

        var numberPart = houseNumber;
        if (!string.IsNullOrWhiteSpace(orientationNumber))
            numberPart = string.IsNullOrWhiteSpace(numberPart)
                ? $"{orientationNumber}{orientationLetter}"
                : $"{numberPart}/{orientationNumber}{orientationLetter}";

        var firstLine = string.Join(" ", new[] { streetName, numberPart }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(firstLine))
            firstLine = GetString(address, "textovaAdresa");

        if (!string.IsNullOrWhiteSpace(district) && !firstLine.Contains(district, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(firstLine) ? district : $"{firstLine}, {district}";

        return firstLine;
    }

    private static string BuildCity(JsonElement address)
    {
        var administrativeArea = GetString(address, "nazevSpravnihoObvodu");
        if (!string.IsNullOrWhiteSpace(administrativeArea))
            return administrativeArea;

        return GetString(address, "nazevObce");
    }

    private static string BuildPostalCode(JsonElement address)
    {
        if (address.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (address.TryGetProperty("psc", out var postalCode))
        {
            return postalCode.ValueKind switch
            {
                JsonValueKind.Number => postalCode.GetInt32().ToString(),
                JsonValueKind.String => postalCode.GetString() ?? string.Empty,
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return fallback;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.TryGetInt64(out var number) ? number.ToString() : fallback,
            _ => fallback
        };
    }

    private static string NormalizeIco(string? ico)
        => new string((ico ?? string.Empty).Where(char.IsDigit).ToArray());
}
