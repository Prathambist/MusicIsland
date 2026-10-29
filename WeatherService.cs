using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace MusicIsland
{
    /// <summary>
    /// Current conditions from Open-Meteo (free, no API key).
    /// </summary>
    public static class WeatherService
    {
        static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public sealed record Weather(double Temperature, int Code)
        {
            public string Description => Code switch
            {
                0 => "Clear",
                1 => "Mostly clear",
                2 => "Partly cloudy",
                3 => "Cloudy",
                45 or 48 => "Fog",
                >= 51 and <= 57 => "Drizzle",
                >= 61 and <= 67 => "Rain",
                >= 71 and <= 77 => "Snow",
                >= 80 and <= 82 => "Showers",
                85 or 86 => "Snow showers",
                >= 95 => "Thunderstorm",
                _ => ""
            };

            // Segoe Fluent / MDL2 glyphs: sun for clear skies, cloud otherwise
            public string Glyph => Code <= 1 ? "\uE706" : "\uE753";
        }

        public static async Task<(double Latitude, double Longitude, string Name)?> GeocodeAsync(string city)
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&name=" +
                         Uri.EscapeDataString(city.Trim());

            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0)
                return null;

            var first = results[0];

            return (
                first.GetProperty("latitude").GetDouble(),
                first.GetProperty("longitude").GetDouble(),
                first.GetProperty("name").GetString() ?? city);
        }

        public static async Task<Weather?> GetCurrentAsync(double latitude, double longitude, bool fahrenheit)
        {
            try
            {
                string url = "https://api.open-meteo.com/v1/forecast" +
                    "?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) +
                    "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) +
                    "&current=temperature_2m,weather_code" +
                    (fahrenheit ? "&temperature_unit=fahrenheit" : "");

                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var current = doc.RootElement.GetProperty("current");

                return new Weather(
                    current.GetProperty("temperature_2m").GetDouble(),
                    current.GetProperty("weather_code").GetInt32());
            }
            catch
            {
                return null;
            }
        }
    }
}
