using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class WeatherInfo
    {
        public double TemperatureC { get; set; }
        public double WindSpeedKmh { get; set; }
        public int WeatherCode { get; set; }
        public string City { get; set; } = string.Empty;
        public DateTime FetchedAtUtc { get; set; }

        /// <summary>Resource key for a vector geometry in Icons.xaml.</summary>
        public string IconKey => WeatherCode switch
        {
            0 => "IconWeatherSun",
            1 or 2 or 3 => "IconWeatherCloud",
            45 or 48 => "IconWeatherFog",
            >= 51 and <= 67 => "IconWeatherRain",
            >= 71 and <= 77 => "IconWeatherSnow",
            >= 80 and <= 82 => "IconWeatherRain",
            >= 85 and <= 86 => "IconWeatherSnow",
            >= 95 => "IconWeatherStorm",
            _ => "IconWeatherCloud"
        };

        /// <summary>Resource key for a localized description in Strings.*.xaml.</summary>
        public string DescriptionKey => WeatherCode switch
        {
            0 => "WeatherClear",
            1 or 2 or 3 => "WeatherCloudy",
            45 or 48 => "WeatherFog",
            >= 51 and <= 67 => "WeatherRain",
            >= 71 and <= 77 => "WeatherSnow",
            >= 80 and <= 82 => "WeatherRain",
            >= 85 and <= 86 => "WeatherSnow",
            >= 95 => "WeatherStorm",
            _ => "WeatherCloudy"
        };
    }

    public sealed class WeatherService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _cachePath;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        public WeatherService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "AgencyContractor");
            Directory.CreateDirectory(appFolder);
            _cachePath = Path.Combine(appFolder, "weather-cache.json");
        }

        public async Task<WeatherInfo?> GetWeatherAsync()
        {
            try
            {
                var cached = TryReadCache();
                if (cached != null && DateTime.UtcNow - cached.FetchedAtUtc < CacheLifetime)
                    return cached;

                var (lat, lon, city) = await GetLocationAsync().ConfigureAwait(false);
                if (double.IsNaN(lat) || double.IsNaN(lon))
                    return cached; // keep last known value if location lookup fails

                var url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,wind_speed_10m",
                    lat, lon);

                using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var current = doc.RootElement.GetProperty("current");

                var info = new WeatherInfo
                {
                    TemperatureC = current.GetProperty("temperature_2m").GetDouble(),
                    WeatherCode = current.GetProperty("weather_code").GetInt32(),
                    WindSpeedKmh = current.TryGetProperty("wind_speed_10m", out var w) ? w.GetDouble() : 0,
                    City = city,
                    FetchedAtUtc = DateTime.UtcNow
                };

                WriteCache(info);
                return info;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WeatherService.GetWeatherAsync", ex.Message);
                return TryReadCache();
            }
        }

        private async Task<(double lat, double lon, string city)> GetLocationAsync()
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync("http://ip-api.com/json/?fields=status,city,lat,lon")
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var status)
                    && status.GetString() == "success")
                {
                    var lat = root.GetProperty("lat").GetDouble();
                    var lon = root.GetProperty("lon").GetDouble();
                    var city = root.TryGetProperty("city", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    return (lat, lon, city);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WeatherService.GetLocationAsync", ex.Message);
            }

            return (double.NaN, double.NaN, string.Empty);
        }

        private WeatherInfo? TryReadCache()
        {
            try
            {
                if (!File.Exists(_cachePath))
                    return null;
                var json = File.ReadAllText(_cachePath);
                return JsonSerializer.Deserialize<WeatherInfo>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private void WriteCache(WeatherInfo info)
        {
            try
            {
                SafeFileService.WriteJsonAtomic(_cachePath, info, _jsonOptions);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WeatherService.WriteCache", ex.Message);
            }
        }
    }
}
