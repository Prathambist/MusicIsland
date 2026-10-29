using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicIsland
{
    public enum FullscreenRule
    {
        Default,
        Hide,
        Show
    }

    public enum IdleMode
    {
        Notch,
        Hide,
        Clock,
        ClockWeather
    }

    public enum IslandTheme
    {
        Midnight,
        Graphite,
        AlbumTint,
        Translucent
    }

    public enum IslandSize
    {
        Small,
        Default,
        Large
    }

    public enum ScrollTarget
    {
        // Named "Spotify" for settings-file compatibility; means "the player shown on the island"
        Spotify,
        System
    }

    public sealed class FullscreenApp
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public FullscreenRule Rule { get; set; } = FullscreenRule.Default;
    }

    public sealed class AppSettings
    {
        // Full-screen behaviour
        public bool HideInFullscreen { get; set; } = true;
        public List<FullscreenApp> FullscreenApps { get; set; } = new();

        // General
        public bool AutoUpdate { get; set; } = true;

        // Appearance
        public IslandTheme Theme { get; set; } = IslandTheme.Midnight;
        public IslandSize Size { get; set; } = IslandSize.Default;
        public bool ShowLyrics { get; set; } = true;
        public bool FindVideoArt { get; set; } = true;

        // Chat notifications
        public bool NotifyDiscord { get; set; } = true;
        public bool NotifyWhatsApp { get; set; } = true;
        public bool NotifyMessenger { get; set; } = true;
        public bool NotifyBrowsers { get; set; }
        public bool NotifyShowMessage { get; set; } = true;

        // Music sources the island follows
        public bool SourceSpotify { get; set; } = true;
        public bool SourceAppleMusic { get; set; } = true;
        public bool SourceBrowsers { get; set; } = true;
        public bool SourceOtherApps { get; set; }

        // Idle / ambient
        public IdleMode IdleMode { get; set; } = IdleMode.Notch;
        public int IdleDelaySeconds { get; set; } = 15;
        public string WeatherCity { get; set; } = "";
        public double? WeatherLatitude { get; set; }
        public double? WeatherLongitude { get; set; }
        public bool UseFahrenheit { get; set; }
        public bool Use24HourClock { get; set; }

        // Interaction
        public bool ScrollVolumeEnabled { get; set; } = true;
        public ScrollTarget ScrollTarget { get; set; } = ScrollTarget.Spotify;
        public bool RealAudioVisualizer { get; set; } = true;

        // Spotify Web API
        public string SpotifyClientId { get; set; } = "";
        public string? SpotifyRefreshToken { get; set; }

        [JsonIgnore]
        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MusicIsland");

        static string FilePath => Path.Combine(Folder, "settings.json");

        static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppSettings Current { get; private set; } = Load();

        public static event Action? Changed;

        static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(FilePath), options) ?? new AppSettings();
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, options));
            }
            catch
            {
            }

            Changed?.Invoke();
        }

        public FullscreenApp? FindApp(string processName) =>
            FullscreenApps.Find(a => string.Equals(
                a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        // Records an app the first time it is seen full-screen so it shows up in Settings
        public FullscreenApp RememberApp(string processName, string displayName)
        {
            var app = FindApp(processName);

            if (app != null)
                return app;

            app = new FullscreenApp
            {
                ProcessName = processName,
                DisplayName = displayName
            };

            FullscreenApps.Add(app);
            Save();
            return app;
        }

        public bool ShouldHideFor(string processName)
        {
            var rule = FindApp(processName)?.Rule ?? FullscreenRule.Default;

            return rule switch
            {
                FullscreenRule.Hide => true,
                FullscreenRule.Show => false,
                _ => HideInFullscreen
            };
        }
    }
}
