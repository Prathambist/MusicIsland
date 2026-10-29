using System.Text.Json;
using MusicIsland;

namespace MusicIsland.Tests;

public class LyricsServiceTests
{
    // ---------- Title / artist cleanup ----------

    [Theory]
    [InlineData("thestrokesVEVO", "The Strokes - Selfless (Official Audio)", "The Strokes", "Selfless")]
    [InlineData("Some Lyrics Channel", "Daft Punk - Something About Us [Official Video]", "Daft Punk", "Something About Us")]
    [InlineData("Adele - Topic", "Hello", "Adele", "Hello")]
    [InlineData("Daft Punk", "Something About Us", "Daft Punk", "Something About Us")]
    [InlineData("The Beatles", "Let It Be - Remastered 2009", "The Beatles", "Let It Be")]
    [InlineData("Radiohead", "Creep - Live", "Radiohead", "Creep")]
    [InlineData("Artist", "Song (Lyrics)", "Artist", "Song")]
    public void Clean_handles_web_player_metadata(string artist, string title, string wantArtist, string wantTitle)
    {
        var (a, t) = LyricsService.Clean(artist, title, fromBrowser: true);

        Assert.Equal(wantArtist, a);
        Assert.Equal(wantTitle, t);
    }

    [Fact]
    public void Clean_does_not_split_app_titles_on_dash()
    {
        // The Spotify app reports the real artist; "A - B" is part of the song name
        var (a, t) = LyricsService.Clean("Artist", "Part One - Part Two", fromBrowser: false);

        Assert.Equal("Artist", a);
        Assert.Equal("Part One - Part Two", t);
    }

    // ---------- LRC parsing ----------

    [Fact]
    public void ParseLrc_reads_sorts_and_expands_repeated_timestamps()
    {
        string lrc = @"[ar: Someone]
[00:12.50] Second line
[00:01.00] First line
[01:02.25][00:30.00] Chorus
not a lyric line
[00:40.00]";

        var lines = LyricsService.ParseLrc(lrc)!;

        Assert.Equal(
            new[] { "First line", "Second line", "Chorus", "", "Chorus" },
            lines.Select(l => l.Text));

        Assert.Equal(TimeSpan.FromSeconds(1), lines[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lines[1].Time);
        Assert.Equal(TimeSpan.FromSeconds(62.25), lines[4].Time);
    }

    [Fact]
    public void ParseLrc_handles_windows_line_endings()
    {
        var lines = LyricsService.ParseLrc("[00:01.00] One\r\n[00:02.00] Two")!;

        Assert.Equal(new[] { "One", "Two" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void ParseLrc_returns_null_without_timestamps()
    {
        Assert.Null(LyricsService.ParseLrc("just some text"));
    }

    [Fact]
    public void Parse_ignores_instrumentals()
    {
        using var doc = JsonDocument.Parse("""{ "instrumental": true, "syncedLyrics": "[00:01.00] x" }""");

        Assert.Null(LyricsService.Parse(doc.RootElement));
    }

    // ---------- Current line ----------

    [Fact]
    public void LineAt_finds_the_line_being_sung()
    {
        var lines = LyricsService.ParseLrc("[00:01.00] A\n[00:05.00] B\n[00:09.00] C")!;

        Assert.Null(LyricsService.LineAt(lines, TimeSpan.FromSeconds(0.5)));
        Assert.Equal("A", LyricsService.LineAt(lines, TimeSpan.FromSeconds(1))!.Text);
        Assert.Equal("A", LyricsService.LineAt(lines, TimeSpan.FromSeconds(4.99))!.Text);
        Assert.Equal("B", LyricsService.LineAt(lines, TimeSpan.FromSeconds(5))!.Text);
        Assert.Equal("C", LyricsService.LineAt(lines, TimeSpan.FromMinutes(10))!.Text);
    }

    // ---------- Choosing a search result ----------

    static JsonElement Results(string json) => JsonDocument.Parse(json).RootElement.Clone();

    const string SearchResults = """
    [
      { "trackName": "Selfless", "artistName": "The Strokes", "duration": 222, "syncedLyrics": "[00:01.00] album" },
      { "trackName": "Selfless", "artistName": "The Strokes", "duration": 260, "syncedLyrics": "[00:01.00] live" },
      { "trackName": "Selfless", "artistName": "Cover Band", "duration": 223, "syncedLyrics": null },
      { "trackName": "Something Else", "artistName": "The Strokes", "duration": 222, "syncedLyrics": "[00:01.00] other" }
    ]
    """;

    [Fact]
    public void PickSearchResult_prefers_the_closest_length()
    {
        var best = LyricsService.PickSearchResult(Results(SearchResults), "The Strokes", "Selfless", TimeSpan.FromSeconds(224));

        Assert.Equal(222, best!.Value.GetProperty("duration").GetInt32());
    }

    [Fact]
    public void PickSearchResult_rejects_a_very_different_length()
    {
        // e.g. a 6-minute music video with a long intro
        var best = LyricsService.PickSearchResult(Results(SearchResults), "The Strokes", "Selfless", TimeSpan.FromSeconds(360));

        Assert.Null(best);
    }

    [Fact]
    public void PickSearchResult_never_returns_a_different_song()
    {
        var best = LyricsService.PickSearchResult(Results(SearchResults), "The Strokes", "Bad Decisions", TimeSpan.FromSeconds(222));

        Assert.Null(best);
    }

    [Fact]
    public void PickSearchResult_needs_the_artist_when_length_is_unknown()
    {
        var results = Results("""
        [ { "trackName": "Hello", "artistName": "Somebody Else", "duration": 200, "syncedLyrics": "[00:01.00] x" } ]
        """);

        Assert.Null(LyricsService.PickSearchResult(results, "Adele", "Hello", TimeSpan.Zero));
        Assert.Null(LyricsService.PickSearchResult(results, "", "Hello", TimeSpan.Zero));
        Assert.NotNull(LyricsService.PickSearchResult(results, "Somebody Else", "Hello", TimeSpan.Zero));
    }
}
