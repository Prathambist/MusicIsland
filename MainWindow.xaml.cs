

using System;

using System.Diagnostics;

using System.Windows;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Media.Animation;

using System.Windows.Media.Imaging;

using System.Windows.Shapes;

using System.Windows.Threading;



namespace MusicIsland

{

    public partial class MainWindow : Window

    {

        readonly SpotifyController spotify = new();

        readonly DispatcherTimer musicTimer = new()
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        readonly DispatcherTimer collapseTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };

        readonly Rectangle[] bars;
        readonly double[] barHeights = { 7, 14, 10, 17, 9, 15, 6 };
        readonly double[] barDurations = { 0.48, 0.67, 0.55, 0.73, 0.51, 0.64, 0.58 };
        readonly double[] barDelays = { 0, 0.11, 0.23, 0.07, 0.19, 0.31, 0.15 };
        readonly double[] idleHeights = { 4, 7, 5, 9, 6, 8, 4 };

        readonly SolidColorBrush buttonBrush =
            new(Color.FromRgb(40, 38, 48));

        Color accent = Color.FromRgb(190, 165, 255);

        bool expanded;
        bool updating;
        bool playing;
        bool visualizerStarted;

        string currentArtist = "";
        string currentAlbum = "";

        public MainWindow()
        {
            InitializeComponent();

            bars = new[]
            {
                Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7
            };

            collapseTimer.Tick += (s, e) =>
            {
                collapseTimer.Stop();
                if (!Island.IsMouseOver && !(IslandHitArea?.IsMouseOver ?? false))
                {
                    AnimateIsland(false);
                }
            };

            spotify.MediaChanged += QueueTrackUpdate;
            spotify.PlaybackChanged += QueueTrackUpdate;

            Loaded += Window_Loaded;

            Closed += (s, e) =>
            {
                musicTimer.Stop();
                collapseTimer.Stop();
            };
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Width = 450;
            Height = 175;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;

            Island.Width = 230;
            Island.Height = 38;
            Island.Background = new SolidColorBrush(Color.FromRgb(5, 5, 8));
            UpdateIslandShape();

            ExpandedContent.Visibility = Visibility.Hidden;
            ExpandedContent.Opacity = 0;
            CompactContent.Visibility = Visibility.Visible;
            CompactContent.Opacity = 1;



            try

            {

                await spotify.ConnectAsync();

                await UpdateTrackAsync();



                musicTimer.Tick += async (s, args) =>

                {

                    await spotify.RefreshAsync();

                    await UpdateTrackAsync();

                };



                musicTimer.Start();

            }

            catch

            {

                TrackTitle.Text = "Spotify unavailable";

                TrackArtist.Text = "Check Spotify is running";

            }

        }



        void QueueTrackUpdate()

        {

            Dispatcher.BeginInvoke(new Action(async () =>

            {

                await UpdateTrackAsync();

            }));

        }



        void Animate(
            DependencyObject target,
            DependencyProperty property,
            double to,
            double duration = 300,
            EasingFunctionBase? easing = null)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(duration),
                EasingFunction = easing ?? new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            if (target is UIElement element)
                element.BeginAnimation(property, animation);
        }



        void AnimateColor(SolidColorBrush brush, Color target, int duration = 350)

        {

            var animation = new ColorAnimation

            {

                To = target,

                Duration = TimeSpan.FromMilliseconds(duration),

                EasingFunction = new CubicEase

                {

                    EasingMode = EasingMode.EaseInOut

                }

            };



            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);

        }



        static Color Darken(Color color, double factor)

        {

            return Color.FromRgb(

                (byte)(color.R * factor),

                (byte)(color.G * factor),

                (byte)(color.B * factor));

        }



        static Color Brighten(Color color, double amount)

        {

            return Color.FromRgb(

                (byte)(color.R + (255 - color.R) * amount),

                (byte)(color.G + (255 - color.G) * amount),

                (byte)(color.B + (255 - color.B) * amount));

        }



        Color GetAccentColor(BitmapSource? source)

        {

            if (source == null)

                return Color.FromRgb(190, 165, 255);



            try

            {

                var converted = new FormatConvertedBitmap(

                    source,

                    PixelFormats.Bgra32,

                    null,

                    0);



                int stride = converted.PixelWidth * 4;

                byte[] pixels = new byte[stride * converted.PixelHeight];



                converted.CopyPixels(pixels, stride, 0);



                long red = 0;

                long green = 0;

                long blue = 0;

                int count = 0;



                int stepX = Math.Max(1, converted.PixelWidth / 40);

                int stepY = Math.Max(1, converted.PixelHeight / 40);



                for (int y = 0; y < converted.PixelHeight; y += stepY)

                {

                    for (int x = 0; x < converted.PixelWidth; x += stepX)

                    {

                        int index = y * stride + x * 4;



                        byte b = pixels[index];

                        byte g = pixels[index + 1];

                        byte r = pixels[index + 2];



                        int maximum = Math.Max(r, Math.Max(g, b));

                        int minimum = Math.Min(r, Math.Min(g, b));



                        if (maximum < 35 || maximum - minimum < 18)

                            continue;



                        red += r;

                        green += g;

                        blue += b;

                        count++;

                    }

                }



                if (count == 0)

                    return Color.FromRgb(190, 165, 255);



                return Color.FromRgb(

                    (byte)(red / count),

                    (byte)(green / count),

                    (byte)(blue / count));

            }

            catch

            {

                return Color.FromRgb(190, 165, 255);

            }

        }



        void ApplyAlbumTheme(BitmapSource? artwork)

        {

            accent = GetAccentColor(artwork);



            Color vividAccent = Brighten(accent, 0.38);

            Color darkButton = Darken(accent, 0.28);



            foreach (var bar in bars)

                bar.Fill = new SolidColorBrush(vividAccent);



            foreach (var button in FindButtons(Island))

                button.Background = new SolidColorBrush(darkButton);



            AccentOutline.BorderBrush =

                new SolidColorBrush(Color.FromArgb(

                    145, vividAccent.R, vividAccent.G, vividAccent.B));



            var gradient = new LinearGradientBrush

            {

                StartPoint = new Point(0, 0),

                EndPoint = new Point(1, 1)

            };



            gradient.GradientStops.Add(

                new GradientStop(Color.FromArgb(

                    225,

                    Darken(accent, 0.32).R,

                    Darken(accent, 0.32).G,

                    Darken(accent, 0.32).B), 0));



            gradient.GradientStops.Add(

                new GradientStop(Color.FromArgb(205, 5, 5, 8), 0.55));



            gradient.GradientStops.Add(

                new GradientStop(Color.FromArgb(240, 5, 5, 8), 1));



            ExpandedTintOverlay.Background = gradient;

        }



        static System.Collections.Generic.IEnumerable<System.Windows.Controls.Button>

            FindButtons(DependencyObject parent)

        {

            int count = VisualTreeHelper.GetChildrenCount(parent);



            for (int i = 0; i < count; i++)

            {

                var child = VisualTreeHelper.GetChild(parent, i);



                if (child is System.Windows.Controls.Button button)

                    yield return button;



                foreach (var nested in FindButtons(child))

                    yield return nested;

            }

        }



        void Island_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateIslandShape();
        }

        void UpdateIslandShape()
        {
            double w = Island.ActualWidth > 0 ? Island.ActualWidth : Island.Width;
            double h = Island.ActualHeight > 0 ? Island.ActualHeight : Island.Height;

            if (w <= 0 || h <= 0)
                return;

            // Compact: h = 38, radius = 19 (full capsule pill)
            // Expanded: h = 140, radius = 28 (smooth iOS squircle)
            double t = Math.Clamp((h - 38.0) / (140.0 - 38.0), 0.0, 1.0);
            double pillRadius = h / 2.0;
            double expandedRadius = 28.0;
            double radius = (1.0 - t) * pillRadius + t * expandedRadius;

            radius = Math.Max(0.0, Math.Min(radius, Math.Min(w / 2.0, h / 2.0)));

            var cornerRadius = new CornerRadius(radius);
            IslandContainer.CornerRadius = cornerRadius;
            Island.CornerRadius = cornerRadius;
            AccentOutline.CornerRadius = cornerRadius;

            var geometry = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            geometry.Freeze();
            Island.Clip = geometry;
        }

        void AnimateIsland(bool open)
        {
            if (expanded == open)
                return;

            expanded = open;

            double targetWidth = open ? 400 : 230;
            double targetHeight = open ? 140 : 38;

            var ease = open
                ? (EasingFunctionBase)new CubicEase { EasingMode = EasingMode.EaseOut }
                : (EasingFunctionBase)new CubicEase { EasingMode = EasingMode.EaseInOut };

            Animate(Island, FrameworkElement.WidthProperty, targetWidth, 380, ease);
            Animate(Island, FrameworkElement.HeightProperty, targetHeight, 380, ease);

            if (open)
            {
                Animate(CompactContent, UIElement.OpacityProperty, 0, 120);

                ExpandedContent.BeginAnimation(UIElement.OpacityProperty, null);
                ExpandedContent.Opacity = 0;
                ExpandedContent.Visibility = Visibility.Visible;

                Animate(ExpandedContent, UIElement.OpacityProperty, 1, 260);

                Dispatcher.BeginInvoke(new Action(StartTitleMarquee),
                    DispatcherPriority.Loaded);
            }
            else
            {
                StopTitleMarquee();

                Animate(ExpandedContent, UIElement.OpacityProperty, 0, 140);

                var fadeOut = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(140)
                };

                fadeOut.Completed += (s, e) =>
                {
                    if (!expanded)
                        ExpandedContent.Visibility = Visibility.Hidden;
                };

                ExpandedContent.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                CompactContent.Visibility = Visibility.Visible;
                Animate(CompactContent, UIElement.OpacityProperty, 1, 220);
            }
        }

        void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            collapseTimer.Stop();
            AnimateIsland(true);
        }

        void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            collapseTimer.Stop();
            collapseTimer.Start();
        }



        void StartVisualizer()

        {

            if (visualizerStarted)

                return;



            visualizerStarted = true;



            for (int i = 0; i < bars.Length; i++)

            {

                bars[i].BeginAnimation(FrameworkElement.HeightProperty, null);



                var animation = new DoubleAnimation

                {

                    From = 3,

                    To = barHeights[i],

                    Duration = TimeSpan.FromSeconds(barDurations[i]),

                    BeginTime = TimeSpan.FromSeconds(barDelays[i]),

                    AutoReverse = true,

                    RepeatBehavior = RepeatBehavior.Forever,

                    EasingFunction = new SineEase

                    {

                        EasingMode = EasingMode.EaseInOut

                    }

                };



                bars[i].BeginAnimation(

                    FrameworkElement.HeightProperty,

                    animation);

            }

        }



        void StopVisualizer()

        {

            visualizerStarted = false;



            for (int i = 0; i < bars.Length; i++)

            {

                bars[i].BeginAnimation(FrameworkElement.HeightProperty, null);

                bars[i].Height = idleHeights[i];

            }

        }



        void StartTitleMarquee()

        {

            StopTitleMarquee();



            if (!expanded || TitleClip.ActualWidth <= 0)

                return;



            TrackTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));



            double overflow = TrackTitle.DesiredSize.Width - TitleClip.ActualWidth;



            if (overflow <= 8)

            {

                TitleTransform.X = 0;

                return;

            }



            var animation = new DoubleAnimation

            {

                From = 0,

                To = -overflow,

                BeginTime = TimeSpan.FromMilliseconds(500),

                Duration = TimeSpan.FromSeconds(Math.Max(3, overflow / 22)),

                AutoReverse = true,

                RepeatBehavior = RepeatBehavior.Forever,

                EasingFunction = new SineEase

                {

                    EasingMode = EasingMode.EaseInOut

                }

            };



            TitleTransform.BeginAnimation(

                TranslateTransform.XProperty,

                animation);

        }



        void StopTitleMarquee()

        {

            TitleTransform.BeginAnimation(

                TranslateTransform.XProperty,

                null);



            TitleTransform.X = 0;

        }



        async Task UpdateTrackAsync()

        {

            if (updating)

                return;



            updating = true;



            try

            {

                var track = await spotify.GetTrackAsync();



                if (track == null)

                {

                    TrackTitle.Text = "Waiting for Spotify";

                    TrackArtist.Text = "Play something in Spotify";



                    currentArtist = "";

                    currentAlbum = "";



                    CompactAlbumArt.Source = null;

                    AlbumArt.Source = null;

                    ExpandedAlbumBackground.Source = null;



                    playing = false;

                    StopVisualizer();

                    PlayPauseButton.Content = "▶";

                    ApplyAlbumTheme(null);

                    return;

                }



                bool trackChanged =

                    TrackTitle.Text != track.Value.Title ||

                    currentArtist != track.Value.Artist ||

                    currentAlbum != track.Value.Album;



                TrackTitle.Text = track.Value.Title;

                TrackArtist.Text = track.Value.Artist;



                currentArtist = track.Value.Artist;

                currentAlbum = track.Value.Album;



                CompactAlbumArt.Source = track.Value.Artwork;

                AlbumArt.Source = track.Value.Artwork;

                ExpandedAlbumBackground.Source = track.Value.Artwork;



                ApplyAlbumTheme(track.Value.Artwork);



                PlayPauseButton.Content = track.Value.Playing ? "Ⅱ" : "▶";



                if (playing != track.Value.Playing)

                {

                    playing = track.Value.Playing;



                    if (playing)

                        StartVisualizer();

                    else

                        StopVisualizer();

                }



                if (trackChanged && expanded)
                    _ = Dispatcher.BeginInvoke(new Action(StartTitleMarquee),
                        DispatcherPriority.Loaded);

            }

            catch

            {

                TrackTitle.Text = "Spotify unavailable";

                TrackArtist.Text = "Check Spotify is running";

            }

            finally

            {

                updating = false;

            }

        }



        void OpenSpotifySearch(string query)

        {

            if (string.IsNullOrWhiteSpace(query))

                return;



            string url = "https://open.spotify.com/search/" +

                         Uri.EscapeDataString(query);



            try

            {

                Process.Start(new ProcessStartInfo

                {

                    FileName = url,

                    UseShellExecute = true

                });

            }

            catch

            {

            }

        }



        void TrackTitle_Click(object sender, MouseButtonEventArgs e)

        {

            if (!string.IsNullOrWhiteSpace(currentAlbum))

                OpenSpotifySearch("album:" + currentAlbum + " artist:" + currentArtist);

            else

                OpenSpotifySearch(TrackTitle.Text + " " + currentArtist);

        }



        void TrackArtist_Click(object sender, MouseButtonEventArgs e)

        {

            OpenSpotifySearch("artist:" + currentArtist);

        }



        async void Previous_Click(object sender, RoutedEventArgs e)

        {

            await spotify.PreviousAsync();

            await UpdateTrackAsync();

        }



        async void PlayPause_Click(object sender, RoutedEventArgs e)

        {

            await spotify.TogglePlayPauseAsync();

            await UpdateTrackAsync();

        }



        async void Next_Click(object sender, RoutedEventArgs e)

        {

            await spotify.NextAsync();

            await UpdateTrackAsync();

        }

    }

}
