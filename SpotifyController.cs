
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MusicIsland
{
    public sealed class SpotifyController
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        GlobalSystemMediaTransportControlsSession? wiredSession;

        public GlobalSystemMediaTransportControlsSession? Session
        {
            get;
            private set;
        }

        public event Action? MediaChanged;
        public event Action? PlaybackChanged;

        public async Task<bool> ConnectAsync()
        {
            manager =
                await GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync();

            manager.CurrentSessionChanged += (s, e) =>
            {
                _ = RefreshAsync();
            };

            return await RefreshAsync();
        }

        public Task<bool> RefreshAsync()
        {
            if (manager == null)
                return Task.FromResult(false);

            var session = manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains(
                    "Spotify",
                    StringComparison.OrdinalIgnoreCase));

            if (session != wiredSession)
            {
                wiredSession = session;
                Session = session;

                if (session != null)
                {
                    session.MediaPropertiesChanged += (s, e) =>
                        MediaChanged?.Invoke();

                    session.PlaybackInfoChanged += (s, e) =>
                        PlaybackChanged?.Invoke();
                }

                MediaChanged?.Invoke();
                PlaybackChanged?.Invoke();
            }
            else
            {
                Session = session;
            }

            return Task.FromResult(Session != null);
        }

        public async Task<(string Title, string Artist, string Album,
            bool Playing, BitmapSource? Artwork)?> GetTrackAsync()
        {
            if (Session == null && !await RefreshAsync())
                return null;

            var session = Session!;

            var media = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();

            BitmapSource? artwork = null;

            if (media.Thumbnail != null)
            {
                try
                {
                    using var stream =
                        await media.Thumbnail.OpenReadAsync();

                    using var reader =
                        new DataReader(stream.GetInputStreamAt(0));

                    await reader.LoadAsync((uint)stream.Size);

                    byte[] bytes = new byte[(int)stream.Size];
                    reader.ReadBytes(bytes);

                    using var memory = new MemoryStream(bytes);

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = memory;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    artwork = bitmap;
                }
                catch
                {
                    artwork = null;
                }
            }

            return (
                string.IsNullOrWhiteSpace(media.Title)
                    ? "Unknown track"
                    : media.Title,

                string.IsNullOrWhiteSpace(media.Artist)
                    ? "Unknown artist"
                    : media.Artist,

                string.IsNullOrWhiteSpace(media.AlbumTitle)
                    ? ""
                    : media.AlbumTitle,

                playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus
                        .Playing,

                artwork
            );
        }

        public async Task PreviousAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TrySkipPreviousAsync();
        }

        public async Task TogglePlayPauseAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TryTogglePlayPauseAsync();
        }

        public async Task NextAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TrySkipNextAsync();
        }
    }
}
