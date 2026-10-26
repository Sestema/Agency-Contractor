using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Services
{
    public sealed class MappedAddress
    {
        public string Street { get; init; } = string.Empty;
        public string Number { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string Zip { get; init; } = string.Empty;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    /// <summary>
    /// Free OpenStreetMap lookup. One request at a time, no API key.
    /// </summary>
    public static class AddressMapLookup
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        public static async Task<IReadOnlyList<MappedAddress>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length < 2)
                return Array.Empty<MappedAddress>();

            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=5&q="
                + Uri.EscapeDataString(query);
            return await ReadListAsync(url, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<MappedAddress?> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            var url = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1&lat="
                + latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "&lon="
                + longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var list = await ReadListAsync(url, cancellationToken).ConfigureAwait(false);
            return list.Count > 0 ? list[0] : null;
        }

        private static async Task<IReadOnlyList<MappedAddress>> ReadListAsync(string url, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastRequestUtc);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                _lastRequestUtc = DateTime.UtcNow;
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return Parse(doc.RootElement);
            }
            finally
            {
                Gate.Release();
            }
        }

        private static IReadOnlyList<MappedAddress> Parse(JsonElement root)
        {
            var list = new List<MappedAddress>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    Add(list, item);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                Add(list, root);
            }

            return list;
        }

        private static void Add(List<MappedAddress> list, JsonElement item)
        {
            if (!item.TryGetProperty("lat", out var latEl) || !item.TryGetProperty("lon", out var lonEl))
                return;
            if (!double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
                return;
            if (!double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                return;

            var address = item.TryGetProperty("address", out var addressEl) ? addressEl : default;
            var street = Read(address, "road", "pedestrian", "footway", "residential", "neighbourhood");
            var number = Read(address, "house_number");
            var city = Read(address, "city", "town", "village", "municipality", "hamlet");
            var zip = Read(address, "postcode");

            if (string.IsNullOrWhiteSpace(street) && item.TryGetProperty("display_name", out var display))
            {
                var first = (display.GetString() ?? string.Empty).Split(',')[0];
                street = LatinAddress.ToLatin(first);
            }

            var label = string.Join(", ", new[]
            {
                string.Join(" ", new[] { street, number }).Trim(),
                city,
                zip
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

            list.Add(new MappedAddress
            {
                Street = street,
                Number = number,
                City = city,
                Zip = zip,
                Latitude = lat,
                Longitude = lon,
                Label = string.IsNullOrWhiteSpace(label) ? $"{lat:0.0000}, {lon:0.0000}" : label
            });
        }

        private static string Read(JsonElement address, params string[] keys)
        {
            if (address.ValueKind != JsonValueKind.Object)
                return string.Empty;

            foreach (var key in keys)
            {
                if (address.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = LatinAddress.ToLatin(value.GetString());
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return string.Empty;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgencyContractor/0.2.13 (desktop address picker)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }
    }
}
