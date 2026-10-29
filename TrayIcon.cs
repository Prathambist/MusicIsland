using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace MusicIsland
{
    /// <summary>
    /// Notification-area icon: quick access to Settings and to the full-screen
    /// rule for whatever app is currently full-screen.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly FullscreenWatcher fullscreen;
        readonly Action openSettings;
        readonly Action quit;
        readonly Func<UpdateService.Release?> update;
        readonly Action installUpdate;
        readonly Icon image;

        static AppSettings Settings => AppSettings.Current;

        public TrayIcon(FullscreenWatcher fullscreen, Action openSettings, Action quit,
            Func<UpdateService.Release?> update, Action installUpdate)
        {
            this.fullscreen = fullscreen;
            this.openSettings = openSettings;
            this.quit = quit;
            this.update = update;
            this.installUpdate = installUpdate;

            image = CreateIcon();

            icon = new Forms.NotifyIcon
            {
                Icon = image,
                Text = "MusicIsland",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };

            icon.ContextMenuStrip.Opening += (s, e) => BuildMenu();
            icon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    openSettings();
            };

            BuildMenu();
        }

        void BuildMenu()
        {
            var menu = icon.ContextMenuStrip!;
            menu.Items.Clear();

            if (update() is UpdateService.Release release)
            {
                var install = new Forms.ToolStripMenuItem($"Install update ({release.Tag})")
                {
                    Font = new Font(menu.Font, FontStyle.Bold)
                };
                install.Click += (s, e) => installUpdate();
                menu.Items.Add(install);
                menu.Items.Add(new Forms.ToolStripSeparator());
            }

            menu.Items.Add("Settings…", null, (s, e) => openSettings());
            menu.Items.Add(new Forms.ToolStripSeparator());

            var hideAll = new Forms.ToolStripMenuItem("Hide in full-screen apps")
            {
                Checked = Settings.HideInFullscreen
            };

            hideAll.Click += (s, e) =>
            {
                Settings.HideInFullscreen = !Settings.HideInFullscreen;
                Settings.Save();
            };

            menu.Items.Add(hideAll);

            // One-click override for the app that is full-screen right now
            if (fullscreen.ProcessName is string process)
            {
                var app = Settings.RememberApp(process, fullscreen.DisplayName ?? process);
                var sub = new Forms.ToolStripMenuItem($"When {app.DisplayName} is full-screen");

                void Add(string label, FullscreenRule rule)
                {
                    var item = new Forms.ToolStripMenuItem(label) { Checked = app.Rule == rule };
                    item.Click += (s, e) =>
                    {
                        app.Rule = rule;
                        Settings.Save();
                    };
                    sub.DropDownItems.Add(item);
                }

                Add("Use default", FullscreenRule.Default);
                Add("Always hide the island", FullscreenRule.Hide);
                Add("Always show the island", FullscreenRule.Show);
                menu.Items.Add(sub);
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit MusicIsland", null, (s, e) => quit());
        }

        // A tiny black pill, drawn at runtime so the repo needs no .ico asset
        static Icon CreateIcon()
        {
            using var bitmap = new Bitmap(32, 32);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var path = new GraphicsPath();
                var rect = new RectangleF(2, 9, 28, 14);
                float d = rect.Height;
                path.AddArc(rect.X, rect.Y, d, d, 90, 180);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180);
                path.CloseFigure();

                using var fill = new SolidBrush(Color.FromArgb(255, 8, 8, 10));
                using var outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
                g.FillPath(fill, path);
                g.DrawPath(outline, path);

                using var dot = new SolidBrush(Color.FromArgb(255, 190, 165, 255));
                g.FillEllipse(dot, 21, 13, 6, 6);
            }

            var handle = bitmap.GetHicon();
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            return icon;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            image.Dispose();
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
