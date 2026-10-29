using MusicIsland;

namespace MusicIsland.Tests;

public class MediaControllerTests
{
    [Theory]
    [InlineData("Spotify.exe", MediaSource.Spotify, "Spotify", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaSource.Spotify, "Spotify", "Spotify")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", MediaSource.AppleMusic, "Apple Music", "AppleMusic")]
    [InlineData("MSEdge", MediaSource.Browser, "Edge", "msedge")]
    [InlineData("Chrome", MediaSource.Browser, "Chrome", "chrome")]
    [InlineData("Brave", MediaSource.Browser, "Brave", "brave")]
    [InlineData("308046B0AF4A39CB", MediaSource.Browser, "Firefox", "firefox")]
    [InlineData("vlc.exe", MediaSource.Other, "vlc", "vlc")]
    public void Identify_recognises_players(string appId, MediaSource source, string name, string process)
    {
        Assert.Equal((source, name, process), MediaController.Identify(appId));
    }

    // Player-switching rules, with plain objects standing in for media sessions
    sealed class Player
    {
        public required string Name;
        public bool Playing;
        public override string ToString() => Name;
    }

    static Player? Pick(Player[] sessions, Player? shown, Player? current) =>
        MediaController.PickSession(sessions, p => p.Playing, shown, current);

    readonly Player spotify = new() { Name = "Spotify" };
    readonly Player youtube = new() { Name = "YouTube" };
    readonly Player ytMusic = new() { Name = "YouTube Music" };

    [Fact]
    public void Keeps_the_shown_player_while_it_plays()
    {
        spotify.Playing = true;
        youtube.Playing = true;

        Assert.Same(spotify, Pick(new[] { youtube, spotify }, shown: spotify, current: youtube));
    }

    [Fact]
    public void Switches_when_the_shown_player_pauses_and_another_plays()
    {
        spotify.Playing = false;
        youtube.Playing = true;

        Assert.Same(youtube, Pick(new[] { spotify, youtube }, shown: spotify, current: spotify));
    }

    [Fact]
    public void Prefers_windows_current_session_among_new_players()
    {
        youtube.Playing = true;
        ytMusic.Playing = true;

        Assert.Same(ytMusic, Pick(new[] { youtube, ytMusic }, shown: spotify, current: ytMusic));
    }

    [Fact]
    public void Keeps_paused_music_when_nothing_plays()
    {
        Assert.Same(spotify, Pick(new[] { youtube, spotify }, shown: spotify, current: youtube));
    }

    [Fact]
    public void Falls_back_when_the_shown_player_closes()
    {
        Assert.Same(youtube, Pick(new[] { ytMusic, youtube }, shown: spotify, current: youtube));
        Assert.Same(ytMusic, Pick(new[] { ytMusic, youtube }, shown: spotify, current: null));
        Assert.Null(Pick(Array.Empty<Player>(), shown: spotify, current: null));
    }
}
