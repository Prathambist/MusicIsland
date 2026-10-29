using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MusicIsland
{
    /// <summary>
    /// Settings are applied and saved as soon as they change.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        readonly SpotifyWebApi spotifyWeb;
        readonly MainWindow island;

        StackPanel appList = null!;
        TextBlock weatherStatus = null!;
        TextBlock spotifyStatus = null!;
        Button connectButton = null!;
        TextBlock updateStatus = null!;
        Button installButton = null!;
        TextBlock notifyStatus = null!;
        CancellationTokenSource? connecting;

        static AppSettings Settings => AppSettings.Current;

        static readonly Brush Secondary = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
        static readonly Brush Card = new SolidColorBrush(Color.FromRgb(22, 22, 24));

        public SettingsWindow(SpotifyWebApi spotifyWeb, MainWindow island)
        {
            this.spotifyWeb = spotifyWeb;
            this.island = island;

            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                // Dark title bar on Windows 10 20H1+ / 11
                int dark = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
            };

            spotifyWeb.ConnectionChanged += OnConnectionChanged;
            AppSettings.Changed += OnSettingsChanged;

            Closed += (s, e) =>
            {
                connecting?.Cancel();
                spotifyWeb.ConnectionChanged -= OnConnectionChanged;
                AppSettings.Changed -= OnSettingsChanged;
            };

            Build();
        }

        void OnConnectionChanged() => Dispatcher.BeginInvoke(new Action(UpdateSpotifyStatus));

        // Apps get added to the list while this window is open
        void OnSettingsChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (appList.Children.Count != Settings.FullscreenApps.Count)
                BuildAppList();
        }));

        void Build()
        {
            Body.Children.Add(new TextBlock
            {
                Text = "MusicIsland",
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            Body.Children.Add(Note("Changes apply instantly."));

            // ---------- General ----------
            var general = Section("General");
            general.Children.Add(Switch("Launch at login", StartupShortcut.IsEnabled,
                v => StartupShortcut.Set(v)));

            general.Children.Add(Switch("Check for updates automatically", Settings.AutoUpdate,
                v => { Settings.AutoUpdate = v; Settings.Save(); }));

            var updateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            updateRow.Children.Add(PillButton("Check now", CheckUpdatesAsync));

            installButton = PillButton("Install", async () =>
            {
                installButton.IsEnabled = false;
                updateStatus.Text = "Downloading…";
                await island.InstallUpdateAsync();
            });
            installButton.Margin = new Thickness(8, 0, 0, 0);
            installButton.Background = new SolidColorBrush(Color.FromRgb(10, 132, 255));
            installButton.Visibility = island.AvailableUpdate != null ? Visibility.Visible : Visibility.Collapsed;
            updateRow.Children.Add(installButton);

            var logButton = PillButton("Open log folder", () =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppSettings.Folder,
                    UseShellExecute = true
                }));
            logButton.Margin = new Thickness(8, 0, 0, 0);
            updateRow.Children.Add(logButton);
            general.Children.Add(updateRow);

            updateStatus = Note(island.AvailableUpdate is UpdateService.Release r
                ? $"Version {r.Version} is available (you have {UpdateService.CurrentVersion})."
                : $"Version {UpdateService.CurrentVersion}.");
            general.Children.Add(updateStatus);

            // ---------- Appearance ----------
            var appearance = Section("Appearance");
            appearance.Children.Add(Label("Theme"));
            appearance.Children.Add(Segmented(
                new[] { "Midnight", "Graphite", "Album tint", "Translucent" },
                (int)Settings.Theme,
                i => { Settings.Theme = (IslandTheme)i; Settings.Save(); }));

            appearance.Children.Add(Label("Size"));
            appearance.Children.Add(Segmented(
                new[] { "Small", "Default", "Large" },
                (int)Settings.Size,
                i => { Settings.Size = (IslandSize)i; Settings.Save(); }));

            appearance.Children.Add(Switch("Show synced lyrics", Settings.ShowLyrics,
                v => { Settings.ShowLyrics = v; Settings.Save(); }));
            appearance.Children.Add(Note("The current line appears under the song when you open the island. Lyrics by LRCLIB."));

            appearance.Children.Add(Switch("Use album art for music videos", Settings.FindVideoArt,
                v => { Settings.FindVideoArt = v; Settings.Save(); }));
            appearance.Children.Add(Note(
                "YouTube and other video sites only share a video frame, so MusicIsland looks up " +
                "the song's real cover (album or single) in Apple's iTunes catalogue."));

            // ---------- Sources ----------
            var sources = Section("Music sources");
            sources.Children.Add(Switch("Spotify app", Settings.SourceSpotify,
                v => { Settings.SourceSpotify = v; Settings.Save(); }));
            sources.Children.Add(Switch("Apple Music app", Settings.SourceAppleMusic,
                v => { Settings.SourceAppleMusic = v; Settings.Save(); }));
            sources.Children.Add(Switch("Web players in your browser", Settings.SourceBrowsers,
                v => { Settings.SourceBrowsers = v; Settings.Save(); }));
            sources.Children.Add(Note(
                "Spotify Web, YouTube, YouTube Music, Apple Music on the web and more, " +
                "in Chrome, Edge, Firefox, Opera or Brave."));
            sources.Children.Add(Switch("Any other app that reports media to Windows", Settings.SourceOtherApps,
                v => { Settings.SourceOtherApps = v; Settings.Save(); }));
            sources.Children.Add(Note(
                "When several are playing, the island stays on the one it's showing; " +
                "when that pauses, it switches to whichever is still playing."));

            // ---------- Notifications ----------
            var notify = Section("Message notifications");
            notify.Children.Add(Note(
                "New messages drop out of the island for a few seconds; click one to open the chat. " +
                "Works with the desktop apps, and with WhatsApp Web / Messenger installed as an app " +
                "from your browser (menu > Install app)."));
            notify.Children.Add(Switch("Discord", Settings.NotifyDiscord,
                v => { Settings.NotifyDiscord = v; Settings.Save(); UpdateNotifyStatus(); }));
            notify.Children.Add(Switch("WhatsApp", Settings.NotifyWhatsApp,
                v => { Settings.NotifyWhatsApp = v; Settings.Save(); UpdateNotifyStatus(); }));
            notify.Children.Add(Switch("Messenger", Settings.NotifyMessenger,
                v => { Settings.NotifyMessenger = v; Settings.Save(); UpdateNotifyStatus(); }));
            notify.Children.Add(Switch("Other notifications from your browser", Settings.NotifyBrowsers,
                v => { Settings.NotifyBrowsers = v; Settings.Save(); UpdateNotifyStatus(); }));
            notify.Children.Add(Note(
                "Windows doesn't tell apps which website a browser tab's notification came from, " +
                "so sites open in a normal tab show up under the browser's name."));
            notify.Children.Add(Switch("Show message text", Settings.NotifyShowMessage,
                v => { Settings.NotifyShowMessage = v; Settings.Save(); }));

            var accessRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var accessButton = PillButton("Allow access", async () =>
            {
                await island.Notifications.StartAsync();
                UpdateNotifyStatus();

                if (island.Notifications.Status != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-settings:privacy-notifications",
                        UseShellExecute = true
                    });
            });
            DockPanel.SetDock(accessButton, Dock.Left);
            accessRow.Children.Add(accessButton);
            notifyStatus = Note("");
            notifyStatus.Margin = new Thickness(12, 0, 0, 0);
            notifyStatus.VerticalAlignment = VerticalAlignment.Center;
            accessRow.Children.Add(notifyStatus);
            notify.Children.Add(accessRow);
            UpdateNotifyStatus();

            // ---------- Full-screen ----------
            var fullscreen = Section("Full-screen apps");
            fullscreen.Children.Add(Switch(
                "Hide the island when an app is full-screen",
                Settings.HideInFullscreen,
                v => { Settings.HideInFullscreen = v; Settings.Save(); }));

            fullscreen.Children.Add(Note(
                "Override it per app below. Games, videos and slide shows are added " +
                "automatically the first time they go full-screen. You can also change " +
                "the rule for the current full-screen app from the tray icon."));

            appList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            fullscreen.Children.Add(appList);
            BuildAppList();

            var addRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var addBox = new TextBox { ToolTip = "Process name, e.g. vlc or chrome" };
            var addButton = PillButton("Add app", () =>
            {
                string name = addBox.Text.Trim();

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];

                if (name.Length == 0)
                    return;

                Settings.RememberApp(name, name);
                addBox.Text = "";
                BuildAppList();
            });

            addButton.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(addButton, Dock.Right);
            addRow.Children.Add(addButton);
            addRow.Children.Add(addBox);
            fullscreen.Children.Add(addRow);

            // ---------- Idle ----------
            var idle = Section("When nothing is playing");

            idle.Children.Add(Label("Idle look"));
            idle.Children.Add(Segmented(
                new[] { "Camera notch", "Hide", "Clock", "Clock + weather" },
                (int)Settings.IdleMode,
                i =>
                {
                    Settings.IdleMode = (IdleMode)i;
                    Settings.Save();
                    _ = island.RefreshWeatherAsync();
                }));

            idle.Children.Add(Label("Go idle after Spotify is paused for"));
            int[] delays = { 5, 15, 60, 0 };
            idle.Children.Add(Segmented(
                new[] { "5 sec", "15 sec", "1 min", "Only when closed" },
                Math.Max(0, Array.IndexOf(delays, Settings.IdleDelaySeconds)),
                i => { Settings.IdleDelaySeconds = delays[i]; Settings.Save(); }));

            idle.Children.Add(Label("Weather location"));
            var cityRow = new DockPanel();
            var cityBox = new TextBox { Text = Settings.WeatherCity };
            var setCity = PillButton("Set", async () =>
            {
                if (string.IsNullOrWhiteSpace(cityBox.Text))
                {
                    Settings.WeatherCity = "";
                    Settings.WeatherLatitude = Settings.WeatherLongitude = null;
                    Settings.Save();
                    weatherStatus.Text = "Weather turned off.";
                    await island.RefreshWeatherAsync();
                    return;
                }

                weatherStatus.Text = "Looking up…";

                try
                {
                    var place = await WeatherService.GeocodeAsync(cityBox.Text);

                    if (place == null)
                    {
                        weatherStatus.Text = "Couldn't find that place.";
                        return;
                    }

                    Settings.WeatherCity = place.Value.Name;
                    Settings.WeatherLatitude = place.Value.Latitude;
                    Settings.WeatherLongitude = place.Value.Longitude;
                    Settings.Save();
                    cityBox.Text = place.Value.Name;
                    weatherStatus.Text = $"Using {place.Value.Name}.";
                    await island.RefreshWeatherAsync();
                }
                catch
                {
                    weatherStatus.Text = "Weather service unreachable.";
                }
            });

            setCity.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(setCity, Dock.Right);
            cityRow.Children.Add(setCity);
            cityRow.Children.Add(cityBox);
            idle.Children.Add(cityRow);

            weatherStatus = Note(Settings.WeatherLatitude != null
                ? $"Using {Settings.WeatherCity}. Weather by Open-Meteo."
                : "Type a city to show weather. Weather by Open-Meteo.");
            idle.Children.Add(weatherStatus);

            idle.Children.Add(Switch("Fahrenheit", Settings.UseFahrenheit, v =>
            {
                Settings.UseFahrenheit = v;
                Settings.Save();
                _ = island.RefreshWeatherAsync();
            }));

            idle.Children.Add(Switch("24-hour clock", Settings.Use24HourClock,
                v => { Settings.Use24HourClock = v; Settings.Save(); }));

            // ---------- Controls ----------
            var controls = Section("Controls");
            controls.Children.Add(Switch("Scroll over the island to change volume",
                Settings.ScrollVolumeEnabled,
                v => { Settings.ScrollVolumeEnabled = v; Settings.Save(); }));

            controls.Children.Add(Label("Scrolling controls"));
            controls.Children.Add(Segmented(
                new[] { "Player volume", "System volume" },
                (int)Settings.ScrollTarget,
                i => { Settings.ScrollTarget = (ScrollTarget)i; Settings.Save(); }));

            controls.Children.Add(Note(
                "Player volume changes the app's own level in the Windows mixer. For web " +
                "players that's the whole browser."));

            controls.Children.Add(Switch("Real-time audio visualizer",
                Settings.RealAudioVisualizer,
                v => { Settings.RealAudioVisualizer = v; Settings.Save(); }));

            controls.Children.Add(Note(
                "Analyses your speakers' output (WASAPI loopback) so the bars follow the " +
                "actual bass, mids and treble. Turn off for a lighter animated pattern."));

            // ---------- Spotify ----------
            var account = Section("Spotify account (Like button)");
            account.Children.Add(Note(
                "1. Create an app at developer.spotify.com/dashboard (Web API).\n" +
                "2. Add this Redirect URI:  " + SpotifyWebApi.RedirectUri + "\n" +
                "3. Paste its Client ID below and press Connect."));

            account.Children.Add(Label("Client ID"));
            var clientBox = new TextBox { Text = Settings.SpotifyClientId };
            clientBox.LostFocus += (s, e) =>
            {
                if (clientBox.Text.Trim() == Settings.SpotifyClientId)
                    return;

                Settings.SpotifyClientId = clientBox.Text.Trim();
                Settings.SpotifyRefreshToken = null;
                Settings.Save();
                UpdateSpotifyStatus();
            };
            account.Children.Add(clientBox);

            var connectRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            connectButton = PillButton("Connect", async () =>
            {
                Settings.SpotifyClientId = clientBox.Text.Trim();
                Settings.Save();

                if (spotifyWeb.IsConnected)
                {
                    spotifyWeb.Disconnect();
                    return;
                }

                connecting?.Cancel();
                connecting = new CancellationTokenSource();
                connectButton.IsEnabled = false;
                spotifyStatus.Text = "Finish signing in in your browser…";

                try
                {
                    await spotifyWeb.ConnectAsync(connecting.Token);
                }
                catch (OperationCanceledException)
                {
                    spotifyStatus.Text = "Sign-in timed out.";
                    return;
                }
                catch (Exception ex)
                {
                    spotifyStatus.Text = ex.Message;
                    return;
                }
                finally
                {
                    connectButton.IsEnabled = true;
                }

                UpdateSpotifyStatus();
            });

            DockPanel.SetDock(connectButton, Dock.Left);
            connectRow.Children.Add(connectButton);
            spotifyStatus = Note("");
            spotifyStatus.Margin = new Thickness(12, 0, 0, 0);
            spotifyStatus.VerticalAlignment = VerticalAlignment.Center;
            connectRow.Children.Add(spotifyStatus);
            account.Children.Add(connectRow);

            UpdateSpotifyStatus();
        }

        async Task CheckUpdatesAsync()
        {
            updateStatus.Text = "Checking…";
            var release = await island.CheckForUpdatesNowAsync();

            installButton.Visibility = release != null ? Visibility.Visible : Visibility.Collapsed;
            updateStatus.Text = release != null
                ? $"Version {release.Version} is available (you have {UpdateService.CurrentVersion})." +
                  (UpdateService.CanSelfUpdate ? "" : " This copy wasn't installed by the installer, so Install opens the download page.")
                : $"You're up to date (version {UpdateService.CurrentVersion}).";
        }

        void UpdateNotifyStatus()
        {
            notifyStatus.Text = island.Notifications.Status switch
            {
                Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed =>
                    "Windows lets MusicIsland read notifications.",
                Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Denied =>
                    "Windows blocked access. Allow it under Privacy & security > Notifications.",
                _ => "MusicIsland needs permission to read notifications."
            };
        }

        void UpdateSpotifyStatus()
        {
            bool connected = spotifyWeb.IsConnected;
            ((TextBlock)connectButton.Content).Text = connected ? "Disconnect" : "Connect";
            connectButton.Background = connected
                ? new SolidColorBrush(Color.FromRgb(44, 44, 46))
                : new SolidColorBrush(Color.FromRgb(30, 215, 96));
            connectButton.Foreground = connected ? Brushes.White : Brushes.Black;
            spotifyStatus.Text = connected
                ? "Connected — the heart saves to your Liked Songs."
                : "Not connected.";
        }

        void BuildAppList()
        {
            appList.Children.Clear();

            if (Settings.FullscreenApps.Count == 0)
            {
                appList.Children.Add(Note("No full-screen apps seen yet."));
                return;
            }

            foreach (var app in Settings.FullscreenApps.OrderBy(a => a.DisplayName))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(new TextBlock
                {
                    Text = app.DisplayName,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                names.Children.Add(new TextBlock
                {
                    Text = app.ProcessName + ".exe",
                    FontSize = 11,
                    Foreground = Secondary
                });
                row.Children.Add(names);

                var rule = Segmented(
                    new[] { "Default", "Hide", "Show" },
                    (int)app.Rule,
                    i => { app.Rule = (FullscreenRule)i; Settings.Save(); });

                rule.Margin = new Thickness(8, 0, 4, 0);
                Grid.SetColumn(rule, 1);
                row.Children.Add(rule);

                var remove = new Button
                {
                    Content = "\uE711",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 10,
                    Width = 28,
                    Height = 28,
                    Foreground = Secondary,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Forget this app",
                    Template = TransparentTemplate()
                };

                remove.Click += (s, e) =>
                {
                    Settings.FullscreenApps.Remove(app);
                    Settings.Save();
                    BuildAppList();
                };

                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);

                appList.Children.Add(row);
            }
        }

        // ---------- Small UI helpers ----------

        StackPanel Section(string title)
        {
            Body.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Secondary,
                Margin = new Thickness(4, 22, 0, 8)
            });

            var content = new StackPanel();
            Body.Children.Add(new Border
            {
                Background = Card,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 14),
                Child = content
            });

            return content;
        }

        static TextBlock Label(string text) => new()
        {
            Text = text,
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 6)
        };

        static TextBlock Note(string text) => new()
        {
            Text = text,
            FontSize = 11.5,
            Foreground = Secondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };

        CheckBox Switch(string text, bool value, Action<bool> changed)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap },
                Foreground = Brushes.White,
                IsChecked = value,
                Style = (Style)FindResource("Switch"),
                Margin = new Thickness(0, 8, 0, 2)
            };

            box.Click += (s, e) => changed(box.IsChecked == true);
            return box;
        }

        Border Segmented(string[] options, int selected, Action<int> changed)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            string group = Guid.NewGuid().ToString();

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var option = new RadioButton
                {
                    Content = options[i],
                    GroupName = group,
                    IsChecked = i == selected,
                    Style = (Style)FindResource("Segment")
                };

                option.Checked += (s, e) => changed(index);
                panel.Children.Add(option);
            }

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 36, 38)),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = panel
            };
        }

        Button PillButton(string text, Action click)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text },
                Style = (Style)FindResource("Pill")
            };

            button.Click += (s, e) => click();
            return button;
        }

        Button PillButton(string text, Func<Task> click) =>
            PillButton(text, () => { _ = click(); });

        static ControlTemplate TransparentTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            border.FirstChild.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.FirstChild.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = border;
            return template;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
