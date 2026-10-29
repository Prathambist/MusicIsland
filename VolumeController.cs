using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace MusicIsland
{
    /// <summary>
    /// Adjusts the playing app's own mixer volume (Spotify, Apple Music, or the
    /// whole browser for web players; works without Spotify Premium) or the
    /// system master volume.
    /// </summary>
    public sealed class VolumeController : IDisposable
    {
        readonly MMDeviceEnumerator enumerator = new();

        /// <returns>The new volume (0..1) and whether the player's own session was used.</returns>
        public (float Volume, bool Muted, bool IsApp)? Change(float delta, ScrollTarget target, string? processName)
        {
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (target == ScrollTarget.Spotify && !string.IsNullOrEmpty(processName))
                {
                    var sessions = FindSessions(device, processName);

                    if (sessions.Count > 0)
                    {
                        float volume = Math.Clamp(sessions[0].SimpleAudioVolume.Volume + delta, 0f, 1f);

                        foreach (var session in sessions)
                            session.SimpleAudioVolume.Volume = volume;

                        return (volume, sessions[0].SimpleAudioVolume.Mute, true);
                    }
                }

                var endpoint = device.AudioEndpointVolume;
                float master = Math.Clamp(endpoint.MasterVolumeLevelScalar + delta, 0f, 1f);
                endpoint.MasterVolumeLevelScalar = master;

                if (endpoint.Mute && delta > 0)
                    endpoint.Mute = false;

                return (master, endpoint.Mute, false);
            }
            catch
            {
                return null;
            }
        }

        static List<AudioSessionControl> FindSessions(MMDevice device, string processName)
        {
            var result = new List<AudioSessionControl>();
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                try
                {
                    using var process = Process.GetProcessById((int)session.GetProcessID);

                    if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        result.Add(session);
                }
                catch
                {
                }
            }

            return result;
        }

        public void Dispose() => enumerator.Dispose();
    }
}
