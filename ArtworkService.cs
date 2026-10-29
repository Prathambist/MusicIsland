using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace MusicIsland
{
    /// <summary>
    /// Finds the real album / single cover for a music video (YouTube etc. only
    /// hand Windows a 16:9 video thumbnail) using the iTunes Search API
    /// (free, no key).
    /// </summary>
    public static class ArtworkService
    {
        static readonly HttpClient http = CreateClient();
        static readonly Dictionary<string, BitmapSource?> cache = new();

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicIsland/{UpdateService.CurrentVersion}");
            return client;
        }

        static readonly Regex Featuring = new(@"[\(\[]?\s*(feat\.?|ft\.?|featuring)\s.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="sourceName">Player name; used as the artist when a site gives none.</param>
        public static async Task<BitmapSource?> FindAsync(string artist, string title, string sourceName)
        {
            bool artistKnown = !string.Equals(artist, sourceName, StringComparison.OrdinalIgnoreCase);
            var (cleanArtist, cleanTitle) = LyricsService.Clean(artistKnown ? artist : "", title, fromBrowser: true);

            if (cleanTitle.Length == 0)
                return null;

            string term = (cleanArtist + " " + Featuring.Replace(cleanTitle, "")).Trim();

            if (cache.TryGetValue(term, out var cached))
                return cached;

            BitmapSource? result = null;

            try
            {
                string url = "https://itunes.apple.com/search?media=music&entity=song&limit=10&term=" +
                             Uri.EscapeDataString(term);

                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                string? artUrl = SelectArtworkUrl(doc.RootElement, cleanArtist, cleanTitle);

                if (artUrl != null)
                    result = await DownloadAsync(artUrl);
            }
            catch (Exception ex)
            {
                Log.Error($"Cover lookup failed for '{term}'", ex);
                return null;
            }

            cache[term] = result;
            return result;
        }

        /// <summary>The 600px cover of the first search result that is the same song.</summary>
        internal static string? SelectArtworkUrl(JsonElement response, string artist, string title)
        {
            string wantTitle = Normalize(title);
            string wantArtist = Normalize(artist);

            if (wantTitle.Length == 0 || !response.TryGetProperty("results", out var results))
                return null;

            foreach (var item in results.EnumerateArray())
            {
                string track = Normalize(item.TryGetProperty("trackName", out var t) ? t.GetString() : null);
                string by = Normalize(item.TryGetProperty("artistName", out var a) ? a.GetString() : null);

                if (track.Length == 0 || !item.TryGetProperty("artworkUrl100", out var art))
                    continue;

                bool titleMatches = track == wantTitle ||
                                    track.StartsWith(wantTitle) || wantTitle.StartsWith(track);

                // Without a reliable artist, only an exact title is good enough
                bool artistMatches = wantArtist.Length > 0
                    ? by.Length > 0 && (by.Contains(wantArtist) || wantArtist.Contains(by))
                    : track == wantTitle;

                if (titleMatches && artistMatches)
                {
                    // 100px by default; Apple serves larger sizes from the same path
                    return art.GetString()?.Replace("100x100bb", "600x600bb");
                }
            }

            return null;
        }

        static async Task<BitmapSource?> DownloadAsync(string url)
        {
            byte[] bytes = await http.GetByteArrayAsync(url);

            using var memory = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        // "Selfless (feat. X)" and "selfless" compare equal
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            text = Featuring.Replace(text, "");
            var builder = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>
        /// App logos (what Windows shows when a site sends no artwork) are small or
        /// have transparent corners; album art and video frames are opaque.
        /// </summary>
        public static bool LooksLikeIcon(BitmapSource image)
        {
            if (image.PixelWidth < 64 || image.PixelHeight < 64)
                return true;

            try
            {
                var pixels = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = pixels.PixelWidth, h = pixels.PixelHeight;
                var alpha = new byte[4];

                foreach (var (x, y) in new[] { (1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2) })
                {
                    pixels.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), alpha, 4, 0);

                    if (alpha[3] < 250)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>True for square-ish art (album covers), false for video frames.</summary>
        public static bool IsSquare(BitmapSource image) =>
            image.PixelHeight > 0 &&
            Math.Abs((double)image.PixelWidth / image.PixelHeight - 1) < 0.1;
    }
}
