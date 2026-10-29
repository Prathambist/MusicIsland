using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MusicIsland
{
    /// <summary>
    /// Checks GitHub Releases for a newer build and installs it in place.
    /// </summary>
    public static class UpdateService
    {
        const string Repo = "Prathambist/MusicIsland";

        public sealed record Release(Version Version, string Tag, string ZipUrl, string PageUrl);

        static readonly HttpClient http = CreateClient();

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicIsland-Updater");
            return client;
        }

        public static Version CurrentVersion => Normalize(
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

        static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicIsland");

        /// <summary>
        /// Only the copy set up by install.ps1 replaces itself; dev builds just
        /// open the release page.
        /// </summary>
        public static bool CanSelfUpdate =>
            string.Equals(
                Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),
                Path.GetFullPath(InstallDir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        public static async Task<Release?> CheckAsync()
        {
            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/releases/latest"));

                return ParseRelease(doc.RootElement, CurrentVersion);
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                return null;
            }
        }

        /// <summary>
        /// A GitHub "latest release" response, if it is newer than <paramref name="current"/>
        /// and has a .zip to install.
        /// </summary>
        internal static Release? ParseRelease(JsonElement root, Version current)
        {
            string tag = root.GetProperty("tag_name").GetString() ?? "";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;

            string? zip = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zip = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (zip == null || Normalize(version) <= Normalize(current))
                return null;

            return new Release(Normalize(version), tag, zip,
                root.TryGetProperty("html_url", out var page) && page.GetString() is string url
                    ? url
                    : $"https://github.com/{Repo}/releases/latest");
        }

        /// <summary>
        /// Downloads and unpacks the release, then hands over to a small script that
        /// waits for this process to exit, copies the files and restarts the app.
        /// The caller should shut the app down right after this returns.
        /// </summary>
        public static async Task InstallAsync(Release release)
        {
            if (!CanSelfUpdate)
            {
                Process.Start(new ProcessStartInfo { FileName = release.PageUrl, UseShellExecute = true });
                return;
            }

            string work = Path.Combine(Path.GetTempPath(), "MusicIsland-update");

            if (Directory.Exists(work))
                Directory.Delete(work, true);

            string unpacked = Path.Combine(work, "files");
            Directory.CreateDirectory(unpacked);

            string zipPath = Path.Combine(work, "update.zip");

            await using (var download = await http.GetStreamAsync(release.ZipUrl))
            await using (var file = File.Create(zipPath))
                await download.CopyToAsync(file);

            ZipFile.ExtractToDirectory(zipPath, unpacked);

            if (!File.Exists(Path.Combine(unpacked, "MusicIsland.exe")))
                throw new InvalidOperationException("The update package doesn't contain MusicIsland.exe.");

            int pid = Environment.ProcessId;
            string script = Path.Combine(work, "update.cmd");

            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"robocopy \"{unpacked}\" \"{InstallDir}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS >nul\r\n" +
                $"start \"\" \"{Path.Combine(InstallDir, "MusicIsland.exe")}\"\r\n");

            Log.Info($"Installing update {release.Tag}");

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
