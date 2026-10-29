using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicIsland
{
    /// <summary>
    /// Time-synced lyrics from LRCLIB (lrclib.net: free, no API key).
    /// </summary>
    public static class LyricsService
    {
        public sealed record Line(TimeSpan Time, string Text);

        static readonly HttpClient http = CreateClient();

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // LRCLIB asks clients to identify themselves
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"MusicIsland/{UpdateService.CurrentVersion} (https://github.com/Prathambist/MusicIsland)");
            return client;
        }

        static readonly Regex Noise = new(
            @"\s*[\(\[][^\)\]]*(official|video|audio|lyric|visuali[sz]er|remaster|hd|hq|4k|explicit|mv|live)[^\)\]]*[\)\]]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex TimeTag = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

        // "Song - Remastered 2011", "Song - Live at ..." are versions, not "Artist - Song"
        static readonly Regex VersionSuffix = new(
            @"^(\d{4}\s+)?(remaster|live|version|edit|mix|remix|mono|stereo|demo|acoustic|bonus|radio|single|instrumental|from)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Turns web-player metadata such as "The Strokes - Selfless (Official Audio)"
        /// by "thestrokesVEVO" into a clean artist / title pair.
        /// </summary>
        public static (string Artist, string Title) Clean(string artist, string title, bool fromBrowser)
        {
            title = Noise.Replace(title, "").Trim();
            artist = Regex.Replace(artist, @"(VEVO|\s*-\s*Topic)$", "", RegexOptions.IgnoreCase).Trim();

            int dash = title.IndexOf(" - ", StringComparison.Ordinal);

            if (fromBrowser && dash > 0 && !VersionSuffix.IsMatch(title[(dash + 3)..].Trim()))
            {
                artist = title[..dash].Trim();
                title = title[(dash + 3)..].Trim();
            }
            else if (dash > 0 && VersionSuffix.IsMatch(title[(dash + 3)..].Trim()))
            {
                title = title[..dash].Trim();
            }

            return (artist, title);
        }

        /// <summary>
        /// Null means "no synced lyrics for this song". Network / server failures
        /// throw instead, so callers don't remember an outage as "no lyrics".
        /// </summary>
        public static async Task<IReadOnlyList<Line>?> GetAsync(
            string artist, string title, string album, TimeSpan duration, bool fromBrowser)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await LookupAsync(artist, title, album, duration, fromBrowser);
                }
                catch (HttpRequestException ex) when (attempt == 0 && (ex.StatusCode == null || (int)ex.StatusCode >= 500))
                {
                    // LRCLIB is occasionally briefly unavailable (503)
                    await Task.Delay(2500);
                }
            }
        }

        static async Task<IReadOnlyList<Line>?> LookupAsync(
            string artist, string title, string album, TimeSpan duration, bool fromBrowser)
        {
            (artist, title) = Clean(artist, title, fromBrowser);

            if (title.Length == 0)
                return null;

            try
            {
                // 1. Exact match (needs the right duration)
                if (duration > TimeSpan.Zero)
                {
                    string url = "https://lrclib.net/api/get" +
                        "?track_name=" + Uri.EscapeDataString(title) +
                        "&artist_name=" + Uri.EscapeDataString(artist) +
                        (album.Length > 0 ? "&album_name=" + Uri.EscapeDataString(album) : "") +
                        "&duration=" + (int)Math.Round(duration.TotalSeconds);

                    using var response = await http.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        var lines = Parse(doc.RootElement);

                        if (lines != null)
                            return lines;
                    }
                }

                // 2. Search, preferring the closest duration
                string search = "https://lrclib.net/api/search" +
                    "?track_name=" + Uri.EscapeDataString(title) +
                    (artist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(artist) : "");

                using var results = JsonDocument.Parse(await http.GetStringAsync(search));
                var best = PickSearchResult(results.RootElement, artist, title, duration);
                return best != null ? Parse(best.Value) : null;
            }
            catch (Exception ex)
            {
                Log.Error($"Lyrics lookup failed for {artist} - {title}", ex);
                throw;
            }
        }

        /// <summary>
        /// The search result with synced lyrics for the same song (and the same
        /// artist when the length is unknown) whose length is closest.
        /// </summary>
        internal static JsonElement? PickSearchResult(JsonElement results, string artist, string title, TimeSpan duration)
        {
            JsonElement? best = null;
            double bestGap = double.MaxValue;
            string wantTitle = ArtworkService.Normalize(title);
            string wantArtist = ArtworkService.Normalize(artist);

            foreach (var item in results.EnumerateArray())
            {
                string name = ArtworkService.Normalize(
                    item.TryGetProperty("trackName", out var tn) ? tn.GetString() : null);
                string by = ArtworkService.Normalize(
                    item.TryGetProperty("artistName", out var an) ? an.GetString() : null);

                // Search is fuzzy: insist on the same song, and the same artist
                // when we can't confirm it by length
                bool sameSong = name.Length > 0 &&
                                (name == wantTitle || name.StartsWith(wantTitle) || wantTitle.StartsWith(name));
                bool sameArtist = wantArtist.Length > 0 && by.Length > 0 &&
                                  (by.Contains(wantArtist) || wantArtist.Contains(by));

                if (!sameSong || (duration <= TimeSpan.Zero && !sameArtist))
                    continue;

                if (item.TryGetProperty("syncedLyrics", out var synced) &&
                    synced.ValueKind == JsonValueKind.String)
                {
                    double gap = duration > TimeSpan.Zero && item.TryGetProperty("duration", out var d) &&
                                 d.ValueKind == JsonValueKind.Number
                        ? Math.Abs(d.GetDouble() - duration.TotalSeconds)
                        : 0;

                    if (gap < bestGap)
                    {
                        best = item.Clone();
                        bestGap = gap;
                    }
                }
            }

            // A very different length is probably another recording / a long video
            return bestGap <= 6 ? best : null;
        }

        internal static IReadOnlyList<Line>? Parse(JsonElement item)
        {
            if (item.TryGetProperty("instrumental", out var instrumental) &&
                instrumental.ValueKind == JsonValueKind.True)
                return null;

            if (!item.TryGetProperty("syncedLyrics", out var synced) ||
                synced.ValueKind != JsonValueKind.String)
                return null;

            return ParseLrc(synced.GetString() ?? "");
        }

        /// <summary>Parses "[mm:ss.xx] text" lines, sorted by time.</summary>
        internal static IReadOnlyList<Line>? ParseLrc(string lrc)
        {
            var lines = new List<Line>();

            foreach (var raw in lrc.Split('\n'))
            {
                var matches = TimeTag.Matches(raw);

                if (matches.Count == 0)
                    continue;

                string text = TimeTag.Replace(raw, "").Trim();

                // A line can carry several timestamps when it repeats
                foreach (Match m in matches)
                {
                    var time = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) +
                               TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                    lines.Add(new Line(time, text));
                }
            }

            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            return lines.Count > 0 ? lines : null;
        }

        /// <summary>The line being sung at <paramref name="position"/>, or null before the first.</summary>
        public static Line? LineAt(IReadOnlyList<Line> lines, TimeSpan position)
        {
            int lo = 0, hi = lines.Count - 1, found = -1;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;

                if (lines[mid].Time <= position)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return found >= 0 ? lines[found] : null;
        }
    }
}
