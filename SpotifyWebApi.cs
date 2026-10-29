using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MusicIsland
{
    /// <summary>
    /// Minimal Spotify Web API client: Authorization Code + PKCE sign-in on a
    /// loopback redirect, and the library endpoints used by the heart button.
    /// </summary>
    public sealed class SpotifyWebApi
    {
        public const string RedirectUri = "http://127.0.0.1:5543/callback";
        const int RedirectPort = 5543;

        const string Scopes =
            "user-library-read user-library-modify user-read-currently-playing user-read-playback-state";

        static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

        string? accessToken;
        DateTime accessTokenExpiry;

        static AppSettings Settings => AppSettings.Current;

        public bool IsConnected =>
            !string.IsNullOrWhiteSpace(Settings.SpotifyClientId) &&
            !string.IsNullOrEmpty(Settings.SpotifyRefreshToken);

        public event Action? ConnectionChanged;

        // ---------- Sign-in ----------

        public async Task ConnectAsync(CancellationToken cancel)
        {
            string clientId = Settings.SpotifyClientId.Trim();

            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("Enter your Spotify Client ID first.");

            string verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomNumberGenerator.GetBytes(16));

            var listener = new TcpListener(IPAddress.Loopback, RedirectPort);
            listener.Start();

            try
            {
                string url = "https://accounts.spotify.com/authorize" +
                    "?client_id=" + Uri.EscapeDataString(clientId) +
                    "&response_type=code" +
                    "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                    "&code_challenge_method=S256" +
                    "&code_challenge=" + challenge +
                    "&state=" + state +
                    "&scope=" + Uri.EscapeDataString(Scopes);

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));

                string? code = null;
                string? error = null;

                while (code == null && error == null)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var stream = client.GetStream();

                    var buffer = new byte[8192];
                    int read = await stream.ReadAsync(buffer, timeout.Token);
                    string request = Encoding.ASCII.GetString(buffer, 0, read);

                    // "GET /callback?code=...&state=... HTTP/1.1"
                    string target = request.Split(' ', 3).ElementAtOrDefault(1) ?? "";

                    if (!target.StartsWith("/callback", StringComparison.Ordinal))
                    {
                        await Respond(stream, 404, "Not found");
                        continue;
                    }

                    var query = ParseQuery(target);

                    if (query.GetValueOrDefault("state") != state)
                        error = "State mismatch";
                    else if (query.TryGetValue("error", out var e))
                        error = e;
                    else
                        code = query.GetValueOrDefault("code");

                    await Respond(stream, 200, code != null
                        ? "MusicIsland is connected to Spotify. You can close this tab."
                        : "Spotify sign-in failed: " + WebUtility.HtmlEncode(error ?? "no code"));
                }

                if (code == null)
                    throw new InvalidOperationException("Spotify sign-in failed: " + error);

                var token = await PostToken(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = verifier
                });

                ApplyToken(token);
            }
            finally
            {
                listener.Stop();
            }

            ConnectionChanged?.Invoke();
        }

        public void Disconnect()
        {
            accessToken = null;
            Settings.SpotifyRefreshToken = null;
            Settings.Save();
            ConnectionChanged?.Invoke();
        }

        static async Task Respond(NetworkStream stream, int status, string message)
        {
            string body =
                "<html><body style=\"background:#000;color:#fff;font-family:Segoe UI;" +
                "display:flex;align-items:center;justify-content:center;height:90vh\">" +
                "<h2 style=\"font-weight:600\">" + message + "</h2></body></html>";

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            string header =
                $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
            await stream.WriteAsync(bytes);
        }

        static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>();
            int q = target.IndexOf('?');

            if (q < 0)
                return result;

            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                result[Uri.UnescapeDataString(parts[0])] =
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            }

            return result;
        }

        // ---------- Tokens ----------

        static async Task<JsonElement> PostToken(Dictionary<string, string> form)
        {
            using var response = await http.PostAsync(
                "https://accounts.spotify.com/api/token",
                new FormUrlEncodedContent(form));

            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Spotify token request failed: " + json);

            return JsonDocument.Parse(json).RootElement.Clone();
        }

        void ApplyToken(JsonElement token)
        {
            accessToken = token.GetProperty("access_token").GetString();
            accessTokenExpiry = DateTime.UtcNow.AddSeconds(
                token.GetProperty("expires_in").GetInt32() - 60);

            if (token.TryGetProperty("refresh_token", out var refresh))
            {
                Settings.SpotifyRefreshToken = Protect(refresh.GetString() ?? "");
                Settings.Save();
            }
        }

        async Task<string?> GetAccessTokenAsync()
        {
            if (accessToken != null && DateTime.UtcNow < accessTokenExpiry)
                return accessToken;

            if (!IsConnected)
                return null;

            string? refresh = Unprotect(Settings.SpotifyRefreshToken!);

            if (string.IsNullOrEmpty(refresh))
                return null;

            try
            {
                var token = await PostToken(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refresh,
                    ["client_id"] = Settings.SpotifyClientId.Trim()
                });

                ApplyToken(token);
                return accessToken;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("invalid_grant"))
            {
                // Access was revoked from the Spotify account page
                Disconnect();
                return null;
            }
        }

        // Refresh tokens are encrypted for the current Windows user
        static string Protect(string value) =>
            Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

        static string? Unprotect(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return null;
            }
        }

        static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // ---------- API calls ----------

        async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string path)
        {
            string? token = await GetAccessTokenAsync();

            if (token == null)
                return null;

            var request = new HttpRequestMessage(method, "https://api.spotify.com/v1" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request);
        }

        /// <summary>The Spotify URI and title of the track playing right now.</summary>
        public async Task<(string Uri, string Title)?> GetCurrentTrackAsync()
        {
            using var response = await SendAsync(HttpMethod.Get, "/me/player/currently-playing");

            if (response == null || response.StatusCode != HttpStatusCode.OK)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (!doc.RootElement.TryGetProperty("item", out var item) ||
                item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("uri", out var uri))
                return null;

            return (uri.GetString() ?? "", item.GetProperty("name").GetString() ?? "");
        }

        public async Task<bool?> IsSavedAsync(string uri)
        {
            using var response = await SendAsync(HttpMethod.Get,
                "/me/library/contains?uris=" + Uri.EscapeDataString(uri));

            if (response == null || !response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return doc.RootElement.ValueKind == JsonValueKind.Array &&
                   doc.RootElement.GetArrayLength() > 0 &&
                   doc.RootElement[0].GetBoolean();
        }

        public async Task<bool> SetSavedAsync(string uri, bool saved)
        {
            using var response = await SendAsync(saved ? HttpMethod.Put : HttpMethod.Delete,
                "/me/library?uris=" + Uri.EscapeDataString(uri));

            return response?.IsSuccessStatusCode == true;
        }
    }
}
