using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MusicIsland
{
    public partial class MainWindow : Window
    {
        // Every shape the island can morph between
        enum Surface
        {
            Hidden,
            Notch,
            CompactMusic,
            CompactAmbient,
            Volume,
            Peek,
            Notify,
            ExpandedMusic,
            ExpandedAmbient
        }

        // A short "live activity" that drops out of the island
        sealed record NotifyItem(
            string App,
            string Sender,
            string Message,
            BitmapSource? Icon,
            Color Badge,
            int Glyph,
            Action OnClick);

        sealed class Panel
        {
            public required FrameworkElement Element;
            public required Spring Opacity;
            public double MinScale;
        }

        readonly MediaController media = new();
        readonly SpotifyWebApi spotifyWeb = new();
        readonly AudioVisualizer visualizer = new();
        readonly VolumeController volume = new();
        readonly NotificationWatcher notifications = new();
        readonly FullscreenWatcher fullscreen;
        TrayIcon? tray;

        readonly DispatcherTimer musicTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer weatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
        readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(170) };
        readonly DispatcherTimer collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        readonly DispatcherTimer volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        readonly DispatcherTimer peekTimer = new() { Interval = TimeSpan.FromMilliseconds(2600) };
        readonly DispatcherTimer notifyTimer = new() { Interval = TimeSpan.FromMilliseconds(4500) };
        readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(12) };

        // Shape springs (SwiftUI-style response / damping)
        readonly Spring width = new(236);
        readonly Spring height = new(38);
        readonly Spring islandOpacity = new(1, 0.35, 1);
        readonly Spring islandScale = new(1, 0.45, 0.8);
        readonly Spring volumeLevel = new(0, 0.22, 0.9);
        readonly Spring volumeRow = new(0, 0.25, 1);
        readonly Spring glow = new(0, 0.5, 1);
        readonly Spring heartScale = new(1, 0.35, 0.42);
        readonly Spring seekGrow = new(0, 0.2, 1);

        readonly Spring[] springs;
        readonly List<Panel> panels = new();
        readonly Panel compactMusicPanel, compactAmbientPanel, volumePanel, notchPanel,
            peekMusicPanel, peekNotifyPanel, expandedMusicPanel, expandedAmbientPanel;

        readonly Rectangle[] compactBars;
        readonly Rectangle[] expandedBars;
        readonly Rectangle[] peekBars;
        readonly float[] audioLevels = new float[AudioVisualizer.BandCount];
        readonly double[] barLevels = new double[AudioVisualizer.BandCount];
        readonly SolidColorBrush barBrush = new(Color.FromRgb(214, 200, 255));

        Surface surface = (Surface)(-1);
        bool rendering;
        TimeSpan lastFrame;

        TrackInfo? track;
        bool playing;
        DateTime? pausedSince;
        bool hovering;
        bool volumeVisible;
        bool updating;
        bool updatePending;

        string trackKey = "";
        string? likeUri;
        bool liked;
        bool likeBusy;

        WeatherService.Weather? weather;

        // Track-change peek and chat / update notifications
        bool peekVisible;
        NotifyItem? currentNotify;
        readonly Queue<NotifyItem> notifyQueue = new();

        // Real cover art found for music videos, and lyrics, per song so switching
        // between players or back to a song is instant
        readonly Dictionary<string, BitmapSource> covers = new();
        readonly Dictionary<string, IReadOnlyList<LyricsService.Line>?> lyricsCache = new();

        // Synced lyrics
        IReadOnlyList<LyricsService.Line>? lyrics;
        LyricsService.Line? currentLyric;

        // Dragging on the progress bar
        bool seeking;
        double seekFraction;

        // Album accent, also used by the album-tint theme
        Color accentColor = Color.FromRgb(190, 165, 255);

        UpdateService.Release? availableUpdate;
        string? announcedUpdate;

        public NotificationWatcher Notifications => notifications;
        public UpdateService.Release? AvailableUpdate => availableUpdate;

        static AppSettings Settings => AppSettings.Current;

        public MainWindow()
        {
            InitializeComponent();

            springs = new[] { width, height, islandOpacity, islandScale, volumeLevel, volumeRow, glow, heartScale, seekGrow };

            compactMusicPanel = AddPanel(CompactMusic, 0.88);
            compactAmbientPanel = AddPanel(CompactAmbient, 0.88);
            volumePanel = AddPanel(CompactVolume, 0.88);
            notchPanel = AddPanel(NotchPanel, 0.9);
            peekMusicPanel = AddPanel(PeekMusic, 0.9, 0.3);
            peekNotifyPanel = AddPanel(PeekNotify, 0.9, 0.3);
            expandedMusicPanel = AddPanel(ExpandedMusic, 0.9, 0.42);
            expandedAmbientPanel = AddPanel(ExpandedAmbient, 0.9, 0.42);

            compactBars = CreateBars(CompactBars, 3, 1.5);
            expandedBars = CreateBars(ExpandedBars, 3.5, 1.75);
            peekBars = CreateBars(PeekBars, 3, 1.5);

            fullscreen = new FullscreenWatcher(() => new WindowInteropHelper(this).Handle);
            fullscreen.Changed += OnFullscreenChanged;

            hoverTimer.Tick += (s, e) =>
            {
                hoverTimer.Stop();

                if (IslandContainer.IsMouseOver && !volumeVisible && currentNotify == null)
                {
                    hovering = true;
                    UpdateSurface();
                }
            };

            collapseTimer.Tick += (s, e) =>
            {
                collapseTimer.Stop();

                if (seeking)
                {
                    collapseTimer.Start();
                    return;
                }

                if (!IslandContainer.IsMouseOver && IslandContainer.ContextMenu?.IsOpen != true)
                {
                    hovering = false;
                    UpdateSurface();
                }
            };

            volumeTimer.Tick += (s, e) =>
            {
                volumeTimer.Stop();
                volumeVisible = false;
                volumeRow.Target = 0;

                // Still hovering once the scroll gesture ends: open up as usual
                if (IslandContainer.IsMouseOver && !hovering)
                    hoverTimer.Start();

                UpdateSurface();
            };

            peekTimer.Tick += (s, e) =>
            {
                peekTimer.Stop();
                peekVisible = false;
                UpdateSurface();
            };

            notifyTimer.Tick += (s, e) =>
            {
                notifyTimer.Stop();

                // Keep it up while the pointer rests on it
                if (IslandContainer.IsMouseOver && currentNotify != null)
                    notifyTimer.Start();
                else
                    ShowNextNotification();
            };

            notifications.Received += EnqueueChat;
            updateTimer.Tick += async (s, e) => await CheckForUpdatesAsync(announce: true);
            App.SecondInstanceLaunched += OpenSettings;

            clockTimer.Tick += (s, e) =>
            {
                UpdateClock();
                UpdateSurface();
            };

            weatherTimer.Tick += async (s, e) => await RefreshWeatherAsync();

            media.MediaChanged += QueueTrackUpdate;
            media.PlaybackChanged += QueueTrackUpdate;
            spotifyWeb.ConnectionChanged += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                trackKey = "";
                QueueTrackUpdate();
            }));

            AppSettings.Changed += () => Dispatcher.BeginInvoke(new Action(ApplySettings));

            SourceInitialized += (s, e) => MakeToolWindow();
            Loaded += Window_Loaded;

            Closed += (s, e) =>
            {
                musicTimer.Stop();
                clockTimer.Stop();
                weatherTimer.Stop();
                fullscreen.Stop();
                notifications.Stop();
                updateTimer.Stop();
                visualizer.Dispose();
                volume.Dispose();
                tray?.Dispose();
            };
        }

        Panel AddPanel(FrameworkElement element, double minScale, double response = 0.24)
        {
            var panel = new Panel
            {
                Element = element,
                Opacity = new Spring(0, response, 1),
                MinScale = minScale
            };

            element.Opacity = 0;
            element.Visibility = Visibility.Hidden;
            panels.Add(panel);
            return panel;
        }

        Rectangle[] CreateBars(StackPanel host, double barWidth, double gap)
        {
            var result = new Rectangle[AudioVisualizer.BandCount];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Rectangle
                {
                    Width = barWidth,
                    Height = barWidth,
                    RadiusX = barWidth / 2,
                    RadiusY = barWidth / 2,
                    Fill = barBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(gap / 2, 0, gap / 2, 0)
                };

                host.Children.Add(result[i]);
            }

            return result;
        }

        // Keep the island out of Alt+Tab and never steal focus from games / editors
        void MakeToolWindow()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE,
                new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;

            tray = new TrayIcon(fullscreen, OpenSettings, () => Close(),
                () => availableUpdate, () => _ = InstallUpdateAsync());

            islandScale.Snap(SizeScale);

            UpdateClock();
            clockTimer.Start();
            fullscreen.Start();
            ApplySettings();
            _ = RefreshWeatherAsync();
            weatherTimer.Start();

            if (Settings.AutoUpdate)
            {
                _ = Task.Delay(TimeSpan.FromSeconds(20))
                    .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(async () =>
                        await CheckForUpdatesAsync(announce: true))));
                updateTimer.Start();
            }

            try
            {
                await media.ConnectAsync();
                await UpdateTrackAsync();

                musicTimer.Tick += async (s, args) =>
                {
                    await media.RefreshAsync();
                    await UpdateTrackAsync();
                };

                musicTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Media sessions unavailable", ex);
                UpdateSurface();
            }
        }

        static double SizeScale => Settings.Size switch
        {
            IslandSize.Small => 0.88,
            IslandSize.Large => 1.12,
            _ => 1.0
        };

        bool notificationsRequested;

        void ApplySettings()
        {
            if (Settings.RealAudioVisualizer && !visualizer.IsRunning && !visualizer.Start())
                Log.Error("Audio loopback capture could not start");

            if (Settings.NotifyDiscord || Settings.NotifyWhatsApp || Settings.NotifyMessenger || Settings.NotifyBrowsers)
            {
                if (!notificationsRequested)
                {
                    notificationsRequested = true;
                    _ = notifications.StartAsync();
                }
            }
            else
            {
                notifications.Stop();
                notificationsRequested = false;
            }

            if (Settings.AutoUpdate) updateTimer.Start(); else updateTimer.Stop();

            // Switching video cover art on/off applies to the current song right away
            if (!Settings.FindVideoArt)
                covers.Clear();
            else if (track != null && !covers.ContainsKey(trackKey))
                _ = LoadCoverAsync(trackKey, track);

            if (track != null)
                ShowArt(EffectiveArt(track, trackKey));

            ApplyTheme();
            ApplyLyricsLayout();
            islandScale.Target = surface == Surface.Hidden ? 0.7 * SizeScale : SizeScale;
            EnsureRendering();

            UpdateClock();
            UpdateSurface();

            // Music source toggles may change which player is shown
            _ = media.RefreshAsync();
            QueueTrackUpdate();
        }

        void OnFullscreenChanged()
        {
            if (fullscreen.ProcessName != null)
                Settings.RememberApp(fullscreen.ProcessName, fullscreen.DisplayName ?? fullscreen.ProcessName);

            UpdateSurface();
        }

        // ================= State → shape =================

        bool IsIdle()
        {
            if (track == null)
                return true;

            if (playing || pausedSince == null || Settings.IdleDelaySeconds <= 0)
                return false;

            return (DateTime.Now - pausedSince.Value).TotalSeconds >= Settings.IdleDelaySeconds;
        }

        Surface ChooseSurface()
        {
            if (fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName))
                return Surface.Hidden;

            bool idle = IsIdle();

            if (hovering)
                return track != null ? Surface.ExpandedMusic : Surface.ExpandedAmbient;

            if (volumeVisible)
                return Surface.Volume;

            // Messages show up even when the idle island is hidden
            if (currentNotify != null)
                return Surface.Notify;

            if (idle && Settings.IdleMode == IdleMode.Hide)
                return Surface.Hidden;

            if (!idle)
                return peekVisible ? Surface.Peek : Surface.CompactMusic;

            return Settings.IdleMode == IdleMode.Notch ? Surface.Notch : Surface.CompactAmbient;
        }

        void UpdateSurface()
        {
            var next = ChooseSurface();

            if (next == surface)
                return;

            bool wasExpanded = surface is Surface.ExpandedMusic or Surface.ExpandedAmbient;
            surface = next;

            (double w, double h, Panel? active) = next switch
            {
                Surface.Hidden => (100.0, 28.0, null),
                Surface.Notch => (112.0, 30.0, notchPanel),
                Surface.CompactAmbient => (210.0, 38.0, compactAmbientPanel),
                Surface.Volume => (264.0, 38.0, volumePanel),
                Surface.Peek => (340.0, 64.0, peekMusicPanel),
                Surface.Notify => (370.0, 76.0, peekNotifyPanel),
                Surface.ExpandedMusic => (400.0, ExpandedMusic.Height, expandedMusicPanel),
                Surface.ExpandedAmbient => (400.0, 150.0, expandedAmbientPanel),
                _ => (236.0, 38.0, compactMusicPanel)
            };

            bool expanding = next is Surface.ExpandedMusic or Surface.ExpandedAmbient
                or Surface.Peek or Surface.Notify;

            // Opening gets a little overshoot, closing settles calmly, and small
            // compact-to-compact morphs (volume, notch) get a lively bounce.
            if (expanding)
            {
                width.Configure(0.5, 0.74);
                height.Configure(0.5, 0.76);
            }
            else if (wasExpanded)
            {
                width.Configure(0.42, 0.86);
                height.Configure(0.38, 0.9);
            }
            else
            {
                width.Configure(0.4, 0.68);
                height.Configure(0.4, 0.75);
            }

            width.Target = w;
            height.Target = h;

            foreach (var panel in panels)
                panel.Opacity.Target = panel == active ? 1 : 0;

            bool hidden = next == Surface.Hidden;
            islandOpacity.Target = hidden ? 0 : 1;
            islandScale.Target = (hidden ? 0.7 : 1) * SizeScale;
            glow.Target = next == Surface.ExpandedMusic && track?.Artwork != null &&
                          Settings.Theme != IslandTheme.AlbumTint ? 1 : 0;

            if (!hidden)
                IslandContainer.IsHitTestVisible = true;

            if (next == Surface.ExpandedMusic)
                Dispatcher.BeginInvoke(new Action(StartTitleMarquee), DispatcherPriority.Loaded);
            else
                StopTitleMarquee();

            EnsureRendering();
        }

        // ================= Frame loop =================

        void EnsureRendering()
        {
            if (rendering)
                return;

            rendering = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        void OnRendering(object? sender, EventArgs e)
        {
            var time = ((RenderingEventArgs)e).RenderingTime;

            if (time == lastFrame)
                return;

            double dt = lastFrame == TimeSpan.Zero
                ? 1.0 / 60
                : Math.Clamp((time - lastFrame).TotalSeconds, 0, 0.05);

            lastFrame = time;

            bool busy = false;

            foreach (var spring in springs)
            {
                spring.Step(dt);
                busy |= !spring.IsSettled;
            }

            ApplyShape();

            foreach (var panel in panels)
            {
                panel.Opacity.Step(dt);
                busy |= !panel.Opacity.IsSettled;
                ApplyPanel(panel);
            }

            busy |= UpdateBars(dt);
            busy |= UpdateProgress();

            double trackWidth = Math.Max(0, CompactVolumeFill.Parent is FrameworkElement f ? f.ActualWidth : 0);
            CompactVolumeFill.Width = trackWidth * Math.Clamp(volumeLevel.Value, 0, 1);
            VolumeRowFill.Width = VolumeTrack.ActualWidth * Math.Clamp(volumeLevel.Value, 0, 1);
            VolumeRow.Opacity = volumeRow.Value;
            ProgressRow.Opacity = 1 - volumeRow.Value;

            ExpandedGlow.Opacity = glow.Value;
            ProgressBar.Height = 5 + 3 * seekGrow.Value;
            HeartScale.ScaleX = HeartScale.ScaleY = heartScale.Value;

            if (!busy)
            {
                CompositionTarget.Rendering -= OnRendering;
                rendering = false;
            }
        }

        void ApplyShape()
        {
            double w = Math.Max(1, width.Value);
            double h = Math.Max(1, height.Value);

            IslandContainer.Width = w;
            IslandContainer.Height = h;

            // Full capsule when small, opening into a continuous rounded card
            double radius = Math.Min(h / 2, 19 + (h - 38) * 0.16);
            radius = Math.Clamp(radius, 0, Math.Min(w, h) / 2);

            IslandShape.CornerRadius = new CornerRadius(radius);

            var clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            clip.Freeze();
            IslandContent.Clip = clip;

            IslandContainer.Opacity = Math.Clamp(islandOpacity.Value, 0, 1);
            IslandScale.ScaleX = IslandScale.ScaleY = islandScale.Value;

            if (surface == Surface.Hidden && islandOpacity.Value < 0.02)
                IslandContainer.IsHitTestVisible = false;
        }

        static void ApplyPanel(Panel panel)
        {
            double opacity = Math.Clamp(panel.Opacity.Value, 0, 1);
            var element = panel.Element;

            element.Opacity = opacity;
            element.Visibility = opacity > 0.003 ? Visibility.Visible : Visibility.Hidden;
            element.IsHitTestVisible = opacity > 0.6;

            if (element.RenderTransform is ScaleTransform scale)
            {
                double s = panel.MinScale + (1 - panel.MinScale) * opacity;
                scale.ScaleX = scale.ScaleY = s;
            }
        }

        bool UpdateBars(double dt)
        {
            bool visible = compactMusicPanel.Element.Visibility == Visibility.Visible ||
                           expandedMusicPanel.Element.Visibility == Visibility.Visible ||
                           peekMusicPanel.Element.Visibility == Visibility.Visible;

            if (!visible)
                return false;

            bool real = Settings.RealAudioVisualizer && visualizer.IsRunning;

            if (!playing)
                Array.Clear(audioLevels);
            else if (real)
                visualizer.GetLevels(audioLevels);
            else
                SyntheticLevels(lastFrame.TotalSeconds);

            bool moving = playing;

            for (int i = 0; i < barLevels.Length; i++)
            {
                double target = audioLevels[i];

                // Fast attack, softer release, like a real VU meter
                double rate = target > barLevels[i] ? 38 : 11;
                barLevels[i] += (target - barLevels[i]) * (1 - Math.Exp(-rate * dt));
                moving |= barLevels[i] > 0.002;

                compactBars[i].Height = 3 + barLevels[i] * 17;
                expandedBars[i].Height = 3.5 + barLevels[i] * 22;
                peekBars[i].Height = 3 + barLevels[i] * 17;
            }

            return moving;
        }

        // Used when loopback capture is off or unavailable
        void SyntheticLevels(double t)
        {
            for (int i = 0; i < audioLevels.Length; i++)
            {
                double v = 0.45 +
                           0.3 * Math.Sin(t * (5.1 + i * 1.7) + i * 1.3) +
                           0.2 * Math.Sin(t * (8.3 + i * 0.9) + i * 2.1);
                audioLevels[i] = (float)Math.Clamp(v, 0.05, 1);
            }
        }

        bool UpdateProgress()
        {
            if (track == null || expandedMusicPanel.Element.Visibility != Visibility.Visible)
                return false;

            var duration = track.Duration;

            if (duration <= TimeSpan.Zero)
            {
                ProgressFill.Width = 0;
                ElapsedText.Text = "";
                RemainingText.Text = "";
                return false;
            }

            var position = track.Position;

            if (playing && track.PositionUpdated > DateTimeOffset.MinValue)
                position += DateTimeOffset.Now - track.PositionUpdated;

            if (seeking)
                position = duration * seekFraction;

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > duration) position = duration;

            UpdateLyric(position);

            ProgressFill.Width = ProgressTrack.ActualWidth * (position / duration);

            string elapsed = FormatTime(position);
            string remaining = "-" + FormatTime(duration - position);

            if (ElapsedText.Text != elapsed) ElapsedText.Text = elapsed;
            if (RemainingText.Text != remaining) RemainingText.Text = remaining;

            return playing || seeking;
        }

        static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        // ================= Hover / scroll =================

        void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            collapseTimer.Stop();

            if (!hovering && !volumeVisible)
                hoverTimer.Start();
        }

        void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            hoverTimer.Stop();
            collapseTimer.Stop();
            collapseTimer.Start();
        }

        void Island_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Settings.ScrollVolumeEnabled)
                return;

            e.Handled = true;

            // 2% per wheel notch; precision touchpads send smaller deltas
            var result = volume.Change(e.Delta / 120f * 0.02f, Settings.ScrollTarget, track?.ProcessName);

            if (result == null)
                return;

            var (level, muted, _) = result.Value;

            // Scrolling the small island shows the pill instead of opening it
            hoverTimer.Stop();
            volumeVisible = true;
            volumeLevel.Target = level;

            if (hovering)
                volumeRow.Target = 1;

            string glyph = muted || level <= 0.001f ? "\uE74F"
                : level < 0.34f ? "\uE993"
                : level < 0.67f ? "\uE994"
                : "\uE995";

            CompactVolumeGlyph.Text = glyph;
            VolumeRowGlyph.Text = glyph;
            CompactVolumeText.Text = ((int)Math.Round(level * 100)).ToString();
            VolumeRowText.Text = (int)Math.Round(level * 100) + "%";

            volumeTimer.Stop();
            volumeTimer.Start();

            UpdateSurface();
            EnsureRendering();
        }

        // ================= Theme =================

        static Color Darken(Color color, double factor) =>
            Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

        static Color Brighten(Color color, double amount) =>
            Color.FromRgb(
                (byte)(color.R + (255 - color.R) * amount),
                (byte)(color.G + (255 - color.G) * amount),
                (byte)(color.B + (255 - color.B) * amount));

        static Color GetAccentColor(BitmapSource? source)
        {
            var fallback = Color.FromRgb(190, 165, 255);

            if (source == null)
                return fallback;

            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int stride = converted.PixelWidth * 4;
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);

                long red = 0, green = 0, blue = 0, weight = 0;
                int stepX = Math.Max(1, converted.PixelWidth / 40);
                int stepY = Math.Max(1, converted.PixelHeight / 40);

                for (int y = 0; y < converted.PixelHeight; y += stepY)
                {
                    for (int x = 0; x < converted.PixelWidth; x += stepX)
                    {
                        int index = y * stride + x * 4;
                        byte b = pixels[index], g = pixels[index + 1], r = pixels[index + 2];
                        int max = Math.Max(r, Math.Max(g, b));
                        int min = Math.Min(r, Math.Min(g, b));

                        if (max < 35 || max - min < 18)
                            continue;

                        // Favour saturated pixels so the accent is vivid, not muddy
                        int w = max - min;
                        red += r * w;
                        green += g * w;
                        blue += b * w;
                        weight += w;
                    }
                }

                return weight == 0
                    ? fallback
                    : Color.FromRgb((byte)(red / weight), (byte)(green / weight), (byte)(blue / weight));
            }
            catch
            {
                return fallback;
            }
        }

        void ApplyAlbumTheme(BitmapSource? artwork)
        {
            var accent = GetAccentColor(artwork);
            var vivid = Brighten(accent, 0.35);

            accentColor = accent;

            if (Settings.Theme == IslandTheme.AlbumTint)
                ApplyTheme();

            barBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = vivid,
                Duration = TimeSpan.FromMilliseconds(450),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

            var deep = Darken(accent, 0.55);
            var glowBrush = new RadialGradientBrush
            {
                Center = new Point(0.12, 0.05),
                GradientOrigin = new Point(0.12, 0.05),
                RadiusX = 0.9,
                RadiusY = 1.3
            };

            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, deep.R, deep.G, deep.B), 0));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, deep.R, deep.G, deep.B), 0.5));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            glowBrush.Freeze();

            ExpandedGlow.Background = glowBrush;
        }

        // ================= Title marquee =================

        void StartTitleMarquee()
        {
            StopTitleMarquee();

            if (surface != Surface.ExpandedMusic || TitleClip.ActualWidth <= 0)
                return;

            TrackTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double overflow = TrackTitle.DesiredSize.Width - TitleClip.ActualWidth;

            if (overflow <= 4)
                return;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = -overflow,
                BeginTime = TimeSpan.FromMilliseconds(900),
                Duration = TimeSpan.FromSeconds(Math.Max(2.5, overflow / 28)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            TitleTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        void StopTitleMarquee()
        {
            TitleTransform.BeginAnimation(TranslateTransform.XProperty, null);
            TitleTransform.X = 0;
        }

        // ================= Track =================

        void QueueTrackUpdate()
        {
            Dispatcher.BeginInvoke(new Action(async () => await UpdateTrackAsync()));
        }

        async Task UpdateTrackAsync()
        {
            // An event that arrives mid-update (e.g. while switching players) must
            // not be lost, or the island shows the old player until the next poll
            if (updating)
            {
                updatePending = true;
                return;
            }

            updating = true;

            try
            {
                var next = await media.GetTrackAsync();
                bool hadTrack = track != null;
                track = next;

                if (next == null)
                {
                    TrackTitle.Text = "Not playing";
                    TrackArtist.Text = "Play something to get started";
                    CompactAlbumArt.Source = null;
                    AlbumArt.Source = null;
                    playing = false;
                    pausedSince = null;
                    trackKey = "";
                    likeUri = null;
                    lyrics = null;
                    ApplyLyricsLayout();
                    SetLiked(false, animate: false);
                    UpdateSourceSlot();

                    if (hadTrack)
                        ApplyAlbumTheme(null);

                    UpdateSurface();
                    return;
                }

                string key = next.SourceName + "|" + next.Title + "|" + next.Artist;
                bool trackChanged = key != trackKey;

                TrackTitle.Text = next.Title;
                TrackArtist.Text = next.Artist;
                PlayPauseButton.Content = next.Playing ? "\uE769" : "\uE768";

                var art = EffectiveArt(next, key);

                if (trackChanged)
                    ApplyAlbumTheme(art);

                ShowArt(art);

                if (next.Playing)
                    pausedSince = null;
                else if (playing || pausedSince == null)
                    pausedSince = DateTime.Now;

                playing = next.Playing;

                if (trackChanged)
                {
                    bool firstTrack = trackKey.Length == 0;
                    trackKey = key;

                    // iOS-style "now playing" peek when the song changes by itself
                    if (!firstTrack && next.Playing && !hovering)
                    {
                        PeekArt.Source = art;
                        PeekTitle.Text = next.Title;
                        PeekArtist.Text = next.Artist;
                        peekVisible = true;
                        peekTimer.Stop();
                        peekTimer.Start();
                    }

                    lyrics = null;
                    currentLyric = null;
                    ApplyLyricsLayout();
                    _ = LoadLyricsAsync(key, next);
                    _ = LoadCoverAsync(key, next);

                    likeUri = null;
                    SetLiked(false, animate: false);
                    UpdateSourceSlot();
                    _ = RefreshLikedAsync(key, next);

                    if (surface == Surface.ExpandedMusic)
                        _ = Dispatcher.BeginInvoke(new Action(StartTitleMarquee), DispatcherPriority.Loaded);
                }

                UpdateSurface();
                EnsureRendering();
            }
            catch (Exception ex)
            {
                Log.Error("Reading the media session failed", ex);
                TrackTitle.Text = "Player unavailable";
                TrackArtist.Text = "Couldn't read what's playing";
            }
            finally
            {
                updating = false;

                if (updatePending)
                {
                    updatePending = false;
                    QueueTrackUpdate();
                }
            }
        }

        // ================= Like / heart =================

        async Task RefreshLikedAsync(string key, TrackInfo info)
        {
            string title = info.Title;

            // Only Spotify (app, or Spotify Web in a browser) can be liked
            if (info.Source is not (MediaSource.Spotify or MediaSource.Browser))
                return;

            HeartButton.ToolTip = spotifyWeb.IsConnected
                ? "Add to Liked Songs"
                : "Connect Spotify in Settings to like songs";

            if (!spotifyWeb.IsConnected)
                return;

            try
            {
                // The Web API can lag a moment behind the desktop app on skips
                for (int attempt = 0; attempt < 4 && key == trackKey; attempt++)
                {
                    var current = await spotifyWeb.GetCurrentTrackAsync();

                    if (current != null &&
                        string.Equals(current.Value.Title, title, StringComparison.OrdinalIgnoreCase))
                    {
                        var saved = await spotifyWeb.IsSavedAsync(current.Value.Uri);

                        if (key == trackKey)
                        {
                            likeUri = current.Value.Uri;
                            SetLiked(saved == true, animate: false);
                            UpdateSourceSlot();
                        }

                        return;
                    }

                    await Task.Delay(1200);
                }
            }
            catch
            {
            }
        }

        async void Heart_Click(object sender, RoutedEventArgs e)
        {
            if (!spotifyWeb.IsConnected)
            {
                OpenSettings();
                return;
            }

            if (likeBusy || track == null)
                return;

            likeBusy = true;

            try
            {
                if (likeUri == null)
                    await RefreshLikedAsync(trackKey, track);

                if (likeUri == null)
                    return;

                bool target = !liked;
                SetLiked(target, animate: true);

                if (!await spotifyWeb.SetSavedAsync(likeUri, target))
                    SetLiked(!target, animate: false);
            }
            catch
            {
                SetLiked(!liked, animate: false);
            }
            finally
            {
                likeBusy = false;
            }
        }

        // The heart is shown for Spotify (and for browsers once the Web API confirms
        // Spotify Web is playing this song); other players show their name instead
        void UpdateSourceSlot()
        {
            bool spotify = track?.Source == MediaSource.Spotify || likeUri != null;
            bool showHeart = track != null && spotify;

            HeartSlot.Visibility = showHeart ? Visibility.Visible : Visibility.Collapsed;
            SourceLabel.Visibility = track != null && !showHeart ? Visibility.Visible : Visibility.Collapsed;
            SourceText.Text = track?.SourceName ?? "";

            // Globe for browsers, music note for apps
            SourceGlyph.Text = ((char)(track?.Source == MediaSource.Browser ? 0xE774 : 0xE8D6)).ToString();

            // Title / artist links search Spotify, so only offer them for Spotify
            TrackTitle.Cursor = TrackArtist.Cursor = spotify ? Cursors.Hand : null;
            TrackTitle.ToolTip = spotify ? "Open album in Spotify" : null;
            TrackArtist.ToolTip = spotify ? "Open artist in Spotify" : null;
        }

        void SetLiked(bool value, bool animate)
        {
            liked = value;
            HeartGlyph.Text = value ? "\uEB52" : "\uEB51";
            HeartGlyph.Foreground = value
                ? new SolidColorBrush(Color.FromRgb(255, 55, 95))
                : new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
            HeartButton.ToolTip = value ? "Remove from Liked Songs" : "Add to Liked Songs";

            if (!animate)
                return;

            // Squash then spring back with overshoot
            heartScale.Value = value ? 0.45 : 0.75;
            heartScale.Velocity = value ? 6 : 0;
            heartScale.Target = 1;

            if (value)
            {
                var duration = TimeSpan.FromMilliseconds(520);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var grow = new DoubleAnimation(0.4, 1.25, duration) { EasingFunction = ease };

                HeartRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                HeartRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                HeartRing.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.9, 0, duration) { EasingFunction = ease });
            }

            EnsureRendering();
        }

        // ================= Lyrics =================

        async Task LoadLyricsAsync(string key, TrackInfo info)
        {
            if (!Settings.ShowLyrics)
                return;

            if (lyricsCache.TryGetValue(key, out var cached))
            {
                lyrics = cached;
                currentLyric = null;
                ApplyLyricsLayout();
                return;
            }

            // Browsers often report the duration a moment after the title
            if (info.Duration <= TimeSpan.Zero)
            {
                await Task.Delay(1500);

                if (key != trackKey || track == null)
                    return;

                info = track;
            }

            IReadOnlyList<LyricsService.Line>? result;

            try
            {
                result = await LyricsService.GetAsync(
                    info.Artist, info.Title, info.Album, info.Duration, info.Source == MediaSource.Browser);
            }
            catch
            {
                // Service unreachable: try again next time this song comes up
                return;
            }

            // A miss without a known length may just be too early; don't remember it
            if (result != null || info.Duration > TimeSpan.Zero)
            {
                if (lyricsCache.Count > 200)
                    lyricsCache.Clear();

                lyricsCache[key] = result;
            }

            if (key != trackKey)
                return;

            lyrics = result;
            currentLyric = null;
            ApplyLyricsLayout();
        }

        // ================= Cover art for videos =================

        async Task LoadCoverAsync(string key, TrackInfo info)
        {
            if (!Settings.FindVideoArt || info.Source != MediaSource.Browser || covers.ContainsKey(key))
                return;

            // Look up straight away; whether it's needed is decided when showing
            var found = await ArtworkService.FindAsync(info.Artist, info.Title, info.SourceName);

            if (found == null)
                return;

            if (covers.Count > 200)
                covers.Clear();

            covers[key] = found;

            if (key == trackKey && track != null)
            {
                var art = EffectiveArt(track, key);

                if (art == found)
                    ApplyAlbumTheme(found);

                ShowArt(art);
            }
        }

        /// <summary>
        /// The player's own artwork when it's a real cover (Spotify, Apple Music,
        /// YouTube Music); otherwise a looked-up cover for music videos.
        /// </summary>
        BitmapSource? EffectiveArt(TrackInfo info, string key)
        {
            bool ownIsCover = info.Artwork != null &&
                              (info.Source != MediaSource.Browser || ArtworkService.IsSquare(info.Artwork));

            if (ownIsCover)
                return info.Artwork;

            return Settings.FindVideoArt && covers.TryGetValue(key, out var found) ? found : info.Artwork;
        }

        void ShowArt(BitmapSource? art)
        {
            if (CompactAlbumArt.Source == art)
                return;

            CompactAlbumArt.Source = art;
            AlbumArt.Source = art;
            PeekArt.Source = art;
            ApplyAlbumTheme(art);

            if (surface == Surface.ExpandedMusic)
            {
                glow.Target = art != null && Settings.Theme != IslandTheme.AlbumTint ? 1 : 0;
                EnsureRendering();
            }
        }

        void ApplyLyricsLayout()
        {
            bool show = lyrics != null && Settings.ShowLyrics;

            LyricRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ExpandedMusic.Height = show ? 204 : 172;

            if (surface == Surface.ExpandedMusic)
            {
                height.Target = ExpandedMusic.Height;
                EnsureRendering();
            }
        }

        void UpdateLyric(TimeSpan position)
        {
            if (lyrics == null || LyricRow.Visibility != Visibility.Visible)
                return;

            // Show each line a touch early so it lands as it is sung
            var line = LyricsService.LineAt(lyrics, position + TimeSpan.FromMilliseconds(200));

            if (line == currentLyric)
                return;

            currentLyric = line;
            LyricText.Text = line == null || line.Text.Length == 0 ? ((char)0x266A).ToString() : line.Text;

            var duration = TimeSpan.FromMilliseconds(360);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            LyricText.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            LyricShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, duration) { EasingFunction = ease });
        }

        // ================= Seeking =================

        double SeekFractionAt(MouseEventArgs e) =>
            ProgressTrack.ActualWidth <= 0
                ? 0
                : Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);

        void Seek_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (track == null || track.Duration <= TimeSpan.Zero)
                return;

            seeking = true;
            seekFraction = SeekFractionAt(e);
            seekGrow.Target = 1;
            ProgressTrack.CaptureMouse();
            EnsureRendering();
            e.Handled = true;
        }

        void Seek_MouseMove(object sender, MouseEventArgs e)
        {
            if (!seeking)
                return;

            seekFraction = SeekFractionAt(e);
            EnsureRendering();
        }

        async void Seek_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!seeking || track == null)
                return;

            seekFraction = SeekFractionAt(e);
            seeking = false;
            seekGrow.Target = 0;
            ProgressTrack.ReleaseMouseCapture();
            e.Handled = true;

            var target = track.Duration * seekFraction;

            // Move the bar right away; the player confirms a moment later
            track = track with { Position = target, PositionUpdated = DateTimeOffset.Now };
            currentLyric = null;
            EnsureRendering();

            try
            {
                if (!await media.SeekAsync(target))
                    Log.Info("Player refused to seek");
            }
            catch (Exception ex)
            {
                Log.Error("Seek failed", ex);
            }
        }

        void Seek_LostCapture(object sender, MouseEventArgs e)
        {
            if (!seeking)
                return;

            seeking = false;
            seekGrow.Target = 0;
            EnsureRendering();
        }

        // ================= Notifications =================

        void EnqueueChat(ChatNotification n)
        {
            var (badge, url) = n.App switch
            {
                ChatApp.Discord => (Color.FromRgb(88, 101, 242), "discord://"),
                ChatApp.WhatsApp => (Color.FromRgb(37, 211, 102), "whatsapp://"),
                ChatApp.Messenger => (Color.FromRgb(0, 132, 255), "https://www.messenger.com"),
                _ => (Color.FromRgb(72, 72, 74), (string?)null)
            };

            Enqueue(new NotifyItem(
                n.AppName,
                n.Sender,
                Settings.NotifyShowMessage && n.Message.Length > 0 ? n.Message : "New message",
                n.Icon,
                badge,
                0xE8BD,
                () =>
                {
                    if (url != null)
                        OpenUrl(url);
                }));
        }

        void Enqueue(NotifyItem item)
        {
            // Don't pop up over a full-screen game or video
            if (fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName))
                return;

            notifyQueue.Enqueue(item);

            if (currentNotify == null)
                ShowNextNotification();
        }

        void ShowNextNotification()
        {
            if (notifyQueue.Count == 0)
            {
                currentNotify = null;
                UpdateSurface();
                return;
            }

            bool replacing = currentNotify != null;
            var item = notifyQueue.Dequeue();
            currentNotify = item;

            NotifyApp.Text = item.App;
            NotifySender.Text = item.Sender;
            NotifyMessage.Text = item.Message;
            NotifyIcon.Source = item.Icon;
            NotifyBadge.Background = new SolidColorBrush(item.Badge);
            NotifyBadgeGlyph.Text = ((char)item.Glyph).ToString();

            hoverTimer.Stop();
            notifyTimer.Stop();
            notifyTimer.Start();

            // A new message while one is showing gives the island a little nudge
            if (replacing)
            {
                width.Velocity += 260;
                height.Velocity += 90;
            }

            UpdateSurface();
            EnsureRendering();
        }

        void Notify_Click(object sender, MouseButtonEventArgs e)
        {
            var item = currentNotify;

            if (item == null)
                return;

            e.Handled = true;
            notifyTimer.Stop();
            ShowNextNotification();
            item.OnClick();
        }

        // ================= Themes =================

        void ApplyTheme()
        {
            Brush background = Settings.Theme switch
            {
                IslandTheme.Graphite => new SolidColorBrush(Color.FromRgb(28, 28, 30)),
                IslandTheme.Translucent => new SolidColorBrush(Color.FromArgb(208, 20, 20, 22)),
                IslandTheme.AlbumTint => new LinearGradientBrush(
                    Darken(accentColor, 0.42), Darken(accentColor, 0.14), new Point(0, 0), new Point(0.4, 1)),
                _ => Brushes.Black
            };

            if (background.CanFreeze)
                background.Freeze();

            IslandShape.Background = background;
        }

        // ================= Updates =================

        async Task CheckForUpdatesAsync(bool announce)
        {
            availableUpdate = await UpdateService.CheckAsync();

            if (availableUpdate == null || !announce || announcedUpdate == availableUpdate.Tag)
                return;

            announcedUpdate = availableUpdate.Tag;

            Enqueue(new NotifyItem(
                "MusicIsland",
                "Update available",
                $"Version {availableUpdate.Version} is ready. Click to install.",
                null,
                Color.FromRgb(10, 132, 255),
                0xE896,
                () => _ = InstallUpdateAsync()));
        }

        public async Task<UpdateService.Release?> CheckForUpdatesNowAsync()
        {
            await CheckForUpdatesAsync(announce: false);
            return availableUpdate;
        }

        public async Task InstallUpdateAsync()
        {
            if (availableUpdate == null)
                return;

            try
            {
                await UpdateService.InstallAsync(availableUpdate);

                if (UpdateService.CanSelfUpdate)
                    Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("Installing the update failed", ex);

                Enqueue(new NotifyItem(
                    "MusicIsland",
                    "Update failed",
                    "Click to download it from GitHub instead.",
                    null,
                    Color.FromRgb(255, 69, 58),
                    0xE783,
                    () => OpenUrl(availableUpdate.PageUrl)));
            }
        }

        // ================= Clock / weather =================

        void UpdateClock()
        {
            var now = DateTime.Now;
            string time = now.ToString(Settings.Use24HourClock ? "HH:mm" : "h:mm");

            CompactTime.Text = time;
            AmbientTime.Text = time;
            AmbientDate.Text = now.ToString("dddd, MMMM d");

            bool showWeather = Settings.IdleMode == IdleMode.ClockWeather && weather != null;

            CompactAmbientGlyph.Text = showWeather ? weather!.Glyph : "";
            CompactAmbientGlyph.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            CompactAmbientText.Text = showWeather
                ? Math.Round(weather!.Temperature) + "°"
                : now.ToString("ddd d");

            AmbientWeather.Visibility = weather != null ? Visibility.Visible : Visibility.Collapsed;

            if (weather != null)
            {
                AmbientWeatherGlyph.Text = weather.Glyph;
                AmbientTemp.Text = Math.Round(weather.Temperature) + "°";
                AmbientWeatherText.Text = string.IsNullOrWhiteSpace(Settings.WeatherCity)
                    ? weather.Description
                    : weather.Description + " · " + Settings.WeatherCity;
            }
        }

        public async Task RefreshWeatherAsync()
        {
            if (Settings.WeatherLatitude is not double lat || Settings.WeatherLongitude is not double lon)
            {
                weather = null;
            }
            else
            {
                weather = await WeatherService.GetCurrentAsync(lat, lon, Settings.UseFahrenheit) ?? weather;
            }

            UpdateClock();
        }

        // ================= Actions =================

        SettingsWindow? settingsWindow;

        void OpenSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(spotifyWeb, this);
                settingsWindow.Closed += (s, e) => settingsWindow = null;
                settingsWindow.Show();
            }

            if (settingsWindow.WindowState == WindowState.Minimized)
                settingsWindow.WindowState = WindowState.Normal;

            // Bring it in front even when another app has focus
            settingsWindow.Topmost = true;
            settingsWindow.Activate();
            settingsWindow.Topmost = false;
        }

        void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        void Quit_Click(object sender, RoutedEventArgs e) => Close();

        static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
            }
        }

        void OpenSpotifySearch(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
                OpenUrl("https://open.media.com/search/" + Uri.EscapeDataString(query));
        }

        void OpenSpotify_Click(object sender, RoutedEventArgs e) => OpenUrl("spotify:");

        bool IsSpotifyTrack => track?.Source == MediaSource.Spotify || likeUri != null;

        void TrackTitle_Click(object sender, MouseButtonEventArgs e)
        {
            if (track == null || !IsSpotifyTrack)
                return;

            if (!string.IsNullOrWhiteSpace(track.Album))
                OpenSpotifySearch("album:" + track.Album + " artist:" + track.Artist);
            else
                OpenSpotifySearch(track.Title + " " + track.Artist);
        }

        void TrackArtist_Click(object sender, MouseButtonEventArgs e)
        {
            if (track != null && IsSpotifyTrack)
                OpenSpotifySearch("artist:" + track.Artist);
        }

        async void Previous_Click(object sender, RoutedEventArgs e)
        {
            await media.PreviousAsync();
            await UpdateTrackAsync();
        }

        async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            await media.TogglePlayPauseAsync();
            await UpdateTrackAsync();
        }

        async void Next_Click(object sender, RoutedEventArgs e)
        {
            await media.NextAsync();
            await UpdateTrackAsync();
        }

        const int GWL_EXSTYLE = -20;
        const long WS_EX_TOOLWINDOW = 0x00000080;
        const long WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    }
}
