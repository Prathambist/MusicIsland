using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace MusicIsland
{
    /// <summary>
    /// Polls the foreground window and reports when an app covers the whole
    /// monitor the island lives on (games, F11 browsers, video players,
    /// PowerPoint slide shows, ...).
    /// </summary>
    public sealed class FullscreenWatcher
    {
        readonly DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        readonly Func<IntPtr> ownWindow;

        public string? ProcessName { get; private set; }
        public string? DisplayName { get; private set; }

        public event Action? Changed;

        public FullscreenWatcher(Func<IntPtr> ownWindow)
        {
            this.ownWindow = ownWindow;
            timer.Tick += (s, e) => Poll();
        }

        public void Start()
        {
            Poll();
            timer.Start();
        }

        public void Stop() => timer.Stop();

        void Poll()
        {
            string? process = null;
            string? display = null;

            try
            {
                var hwnd = DetectFullscreenWindow();

                if (hwnd != IntPtr.Zero)
                    (process, display) = DescribeProcess(hwnd);
            }
            catch
            {
            }

            if (process == ProcessName)
                return;

            ProcessName = process;
            DisplayName = display;
            Changed?.Invoke();
        }

        IntPtr DetectFullscreenWindow()
        {
            var hwnd = GetForegroundWindow();
            var own = ownWindow();

            if (hwnd == IntPtr.Zero || hwnd == own || !IsWindowVisible(hwnd))
                return IntPtr.Zero;

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);

            switch (className.ToString())
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "Windows.UI.Core.CoreWindow":
                case "XamlExplorerHostIslandWindow":
                    return IntPtr.Zero;
            }

            // Only care about the monitor the island is shown on
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (own != IntPtr.Zero &&
                monitor != MonitorFromWindow(own, MONITOR_DEFAULTTONEAREST))
                return IntPtr.Zero;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var rect))
                return IntPtr.Zero;

            bool coversMonitor =
                rect.Left <= info.rcMonitor.Left &&
                rect.Top <= info.rcMonitor.Top &&
                rect.Right >= info.rcMonitor.Right &&
                rect.Bottom >= info.rcMonitor.Bottom;

            if (coversMonitor)
            {
                // A normal maximised window with an auto-hiding taskbar also
                // covers the monitor; it still has a caption, so skip it.
                long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
                bool captioned = (style & WS_CAPTION) == WS_CAPTION;

                if (IsZoomed(hwnd) && captioned)
                    return IntPtr.Zero;

                return hwnd;
            }

            // Exclusive-mode D3D games and presentation mode
            if (SHQueryUserNotificationState(out int state) == 0 &&
                (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                return hwnd;

            return IntPtr.Zero;
        }

        static (string Process, string Display) DescribeProcess(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);

            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            string display = name;

            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription;

                if (!string.IsNullOrWhiteSpace(description))
                    display = description.Trim();
            }
            catch
            {
                // Elevated or protected processes cannot be inspected
            }

            return (name, display);
        }

        const int MONITOR_DEFAULTTONEAREST = 2;
        const int GWL_STYLE = -16;
        const long WS_CAPTION = 0x00C00000;
        const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        const int QUNS_PRESENTATION_MODE = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);
    }
}
