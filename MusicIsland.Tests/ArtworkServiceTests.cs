using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicIsland;

namespace MusicIsland.Tests;

public class ArtworkServiceTests
{
    [Theory]
    [InlineData("Selfless (feat. Someone)", "selfless")]
    [InlineData("Get Lucky ft. Pharrell", "getlucky")]
    [InlineData("AC/DC", "acdc")]
    [InlineData("Beyoncé", "beyoncé")]
    [InlineData(null, "")]
    public void Normalize_compares_titles_loosely(string? input, string expected)
    {
        Assert.Equal(expected, ArtworkService.Normalize(input));
    }

    static JsonElement Response(string json) => JsonDocument.Parse(json).RootElement.Clone();

    const string ITunes = """
    { "results": [
      { "trackName": "Selfless", "artistName": "Tribute Band", "artworkUrl100": "https://x/tribute/100x100bb.jpg" },
      { "trackName": "Selfless", "artistName": "The Strokes", "artworkUrl100": "https://x/strokes/100x100bb.jpg" },
      { "trackName": "Selfless", "artistName": "The Strokes" }
    ] }
    """;

    [Fact]
    public void SelectArtworkUrl_matches_artist_and_asks_for_600px()
    {
        Assert.Equal("https://x/strokes/600x600bb.jpg",
            ArtworkService.SelectArtworkUrl(Response(ITunes), "The Strokes", "Selfless"));
    }

    [Fact]
    public void SelectArtworkUrl_tolerates_channel_style_artist_names()
    {
        // "thestrokes" (from "thestrokesVEVO") still matches "The Strokes"
        Assert.Equal("https://x/strokes/600x600bb.jpg",
            ArtworkService.SelectArtworkUrl(Response(ITunes), "thestrokes", "Selfless"));
    }

    [Fact]
    public void SelectArtworkUrl_rejects_other_artists_and_songs()
    {
        Assert.Null(ArtworkService.SelectArtworkUrl(Response(ITunes), "Daft Punk", "Selfless"));
        Assert.Null(ArtworkService.SelectArtworkUrl(Response(ITunes), "The Strokes", "Reptilia"));
    }

    [Fact]
    public void SelectArtworkUrl_without_artist_needs_exact_title()
    {
        var response = Response("""
        { "results": [ { "trackName": "Hello Again", "artistName": "X", "artworkUrl100": "https://x/a/100x100bb.jpg" } ] }
        """);

        Assert.Null(ArtworkService.SelectArtworkUrl(response, "", "Hello"));
        Assert.NotNull(ArtworkService.SelectArtworkUrl(response, "", "Hello Again"));
    }

    [Fact]
    public void SelectArtworkUrl_ignores_results_with_no_artist_name()
    {
        // Regression: an empty artistName used to match any artist
        var response = Response("""
        { "results": [ { "trackName": "Selfless", "artistName": "", "artworkUrl100": "https://x/a/100x100bb.jpg" } ] }
        """);

        Assert.Null(ArtworkService.SelectArtworkUrl(response, "The Strokes", "Selfless"));
    }

    // ---------- Image heuristics ----------

    static BitmapSource Image(int width, int height, byte cornerAlpha = 255)
    {
        var pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 80;
            pixels[i + 2] = 120;
            pixels[i + 3] = 255;
        }

        // Make the corners (and their neighbours) transparent like an app logo
        foreach (var (x, y) in new[] { (0, 0), (1, 1), (width - 1, 0), (width - 2, 1), (0, height - 1), (1, height - 2), (width - 1, height - 1), (width - 2, height - 2) })
            pixels[(y * width + x) * 4 + 3] = cornerAlpha;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Album_art_is_square_and_video_frames_are_not()
    {
        Assert.True(ArtworkService.IsSquare(Image(600, 600)));
        Assert.True(ArtworkService.IsSquare(Image(544, 520)));
        Assert.False(ArtworkService.IsSquare(Image(480, 360)));
        Assert.False(ArtworkService.IsSquare(Image(1280, 720)));
    }

    [Fact]
    public void Browser_logos_are_recognised_as_icons()
    {
        Assert.True(ArtworkService.LooksLikeIcon(Image(256, 256, cornerAlpha: 0)));
        Assert.True(ArtworkService.LooksLikeIcon(Image(32, 32)));
        Assert.False(ArtworkService.LooksLikeIcon(Image(300, 300)));
        Assert.False(ArtworkService.LooksLikeIcon(Image(1280, 720)));
    }
}
