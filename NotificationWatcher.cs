using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace MusicIsland
{
    public enum ChatApp
    {
        Discord,
        WhatsApp,
        Messenger,
        Browser
    }

    public sealed record ChatNotification(
        ChatApp App,
        string AppName,
        string Sender,
        string Message,
        BitmapSource? Icon,
        bool FromBrowser);

    /// <summary>
    /// Reads Windows toast notifications (with the user's permission) and picks
    /// out Discord, WhatsApp and Messenger messages. Those apps are recognised
    /// by name, which includes web apps installed from the browser ("Install
    /// app"). Windows doesn't say which website a plain browser tab's
    /// notification came from, so those are optional and labelled with the
    /// browser's name.
    /// </summary>
    public sealed class NotificationWatcher
    {
        readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
        readonly HashSet<uint> seen = new();

        UserNotificationListener? listener;
        bool polling;

        public UserNotificationListenerAccessStatus Status { get; private set; } =
            UserNotificationListenerAccessStatus.Unspecified;

        public event Action<ChatNotification>? Received;

        static readonly string[] Browsers =
            { "chrome", "edge", "brave", "firefox", "opera", "vivaldi" };

        public NotificationWatcher()
        {
            timer.Tick += async (s, e) => await PollAsync();
        }

        public async Task<bool> StartAsync()
        {
            if (timer.IsEnabled)
                return true;

            try
            {
                listener = UserNotificationListener.Current;
                Status = await listener.RequestAccessAsync();
                Log.Info("Notification access: " + Status);

                if (Status != UserNotificationListenerAccessStatus.Allowed)
                    return false;

                // Don't replay what's already in the notification center
                foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
                    seen.Add(n.Id);

                timer.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Notification listener unavailable", ex);
                Status = UserNotificationListenerAccessStatus.Denied;
                return false;
            }
        }

        public void Stop() => timer.Stop();

        async Task PollAsync()
        {
            if (polling || listener == null)
                return;

            polling = true;

            try
            {
                var current = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                var ids = new HashSet<uint>();

                foreach (var n in current)
                {
                    ids.Add(n.Id);

                    if (seen.Contains(n.Id))
                        continue;

                    var parsed = await ParseAsync(n);

                    if (parsed != null)
                        Received?.Invoke(parsed);
                }

                // Forget dismissed notifications so the set doesn't grow forever
                seen.Clear();
                seen.UnionWith(ids);
            }
            catch (Exception ex)
            {
                Log.Error("Reading notifications failed", ex);
            }
            finally
            {
                polling = false;
            }
        }

        static async Task<ChatNotification?> ParseAsync(UserNotification n)
        {
            string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);

            if (binding == null)
                return null;

            var texts = binding.GetTextElements().Select(t => t.Text?.Trim() ?? "")
                .Where(t => t.Length > 0).ToList();

            string name = appName.ToLowerInvariant();
            bool fromBrowser = Browsers.Any(name.Contains);

            ChatApp? app =
                name.Contains("discord") ? ChatApp.Discord :
                name.Contains("whatsapp") ? ChatApp.WhatsApp :
                name.Contains("messenger") ? ChatApp.Messenger :
                fromBrowser ? ChatApp.Browser :
                null;

            if (app == null || !IsEnabled(app.Value) || texts.Count == 0)
                return null;

            BitmapSource? icon = null;

            try
            {
                var logo = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
                icon = await MediaController.LoadImageAsync(logo);
            }
            catch
            {
            }

            return new ChatNotification(
                app.Value,
                app.Value switch
                {
                    ChatApp.Discord => "Discord",
                    ChatApp.WhatsApp => "WhatsApp",
                    ChatApp.Messenger => "Messenger",
                    _ => appName
                },
                texts[0],
                string.Join(" ", texts.Skip(1)),
                icon,
                fromBrowser);
        }

        static bool IsEnabled(ChatApp app)
        {
            var settings = AppSettings.Current;

            return app switch
            {
                ChatApp.Discord => settings.NotifyDiscord,
                ChatApp.WhatsApp => settings.NotifyWhatsApp,
                ChatApp.Messenger => settings.NotifyMessenger,
                _ => settings.NotifyBrowsers
            };
        }
    }
}
