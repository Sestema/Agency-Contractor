using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public enum AddressHistoryKind
    {
        Local,
        Abroad
    }

    /// <summary>
    /// Recent map addresses for this computer. Residence and abroad are stored apart.
    /// </summary>
    public static class AddressHistoryStore
    {
        public const int MaxItems = 15;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static IReadOnlyList<MappedAddress> Load(AddressHistoryKind kind)
        {
            try
            {
                var path = FilePath();
                if (!File.Exists(path))
                    return Array.Empty<MappedAddress>();

                var model = JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(path), JsonOptions) ?? new HistoryFile();
                var source = (kind == AddressHistoryKind.Abroad ? model.Abroad : model.Local) ?? new List<MappedAddress>();
                return source
                    .Where(item => item != null && HasContent(item))
                    .Take(MaxItems)
                    .Select(WithLabel)
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddressHistoryStore.Load", ex.Message);
                return Array.Empty<MappedAddress>();
            }
        }

        public static void Remember(AddressHistoryKind kind, MappedAddress address)
        {
            if (!HasContent(address))
                return;

            try
            {
                var path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var model = File.Exists(path)
                    ? JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(path), JsonOptions) ?? new HistoryFile()
                    : new HistoryFile();

                if (kind == AddressHistoryKind.Abroad)
                    model.Abroad = Promote(model.Abroad, address);
                else
                    model.Local = Promote(model.Local, address);

                File.WriteAllText(path, JsonSerializer.Serialize(model, JsonOptions));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddressHistoryStore.Remember", ex.Message);
            }
        }

        public static List<MappedAddress> Promote(IEnumerable<MappedAddress>? current, MappedAddress address, int maxItems = MaxItems)
        {
            var key = Key(address);
            var next = new List<MappedAddress>();
            if (!string.IsNullOrEmpty(key))
                next.Add(WithLabel(address));

            if (current != null)
            {
                foreach (var item in current)
                {
                    if (item == null || !HasContent(item))
                        continue;
                    if (next.Count >= maxItems)
                        break;
                    if (string.Equals(Key(item), key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    next.Add(WithLabel(item));
                }
            }

            return next;
        }

        public static bool HasContent(MappedAddress address) =>
            !string.IsNullOrWhiteSpace(address.Street) || !string.IsNullOrWhiteSpace(address.City);

        private static MappedAddress WithLabel(MappedAddress address)
        {
            if (!string.IsNullOrWhiteSpace(address.Label))
                return address;

            var label = string.Join(", ", new[]
            {
                string.Join(" ", new[] { address.Street, address.Number }.Where(part => !string.IsNullOrWhiteSpace(part))),
                address.City,
                address.Zip
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

            return new MappedAddress
            {
                Street = address.Street,
                Number = address.Number,
                City = address.City,
                Zip = address.Zip,
                Latitude = address.Latitude,
                Longitude = address.Longitude,
                Label = label
            };
        }

        private static string Key(MappedAddress address) =>
            string.Join("|",
                (address.Street ?? string.Empty).Trim(),
                (address.Number ?? string.Empty).Trim(),
                (address.City ?? string.Empty).Trim(),
                (address.Zip ?? string.Empty).Trim());

        private static string FilePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgencyContractor",
                "address-history.json");

        private sealed class HistoryFile
        {
            public List<MappedAddress> Local { get; set; } = new();
            public List<MappedAddress> Abroad { get; set; } = new();
        }
    }
}
