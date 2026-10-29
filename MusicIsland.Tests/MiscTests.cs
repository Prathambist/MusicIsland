using System.Text.Json;
using MusicIsland;

namespace MusicIsland.Tests;

public class UpdateServiceTests
{
    static JsonElement Release(string tag, bool withZip = true) => JsonDocument.Parse($$"""
    {
      "tag_name": "{{tag}}",
      "html_url": "https://github.com/Prathambist/MusicIsland/releases/tag/{{tag}}",
      "assets": [
        { "name": "notes.txt", "browser_download_url": "https://x/notes.txt" }
        {{(withZip ? """, { "name": "MusicIsland-win-x64.zip", "browser_download_url": "https://x/app.zip" }""" : "")}}
      ]
    }
    """).RootElement.Clone();

    static readonly Version Installed = new(1, 1, 0);

    [Fact]
    public void Offers_a_newer_release()
    {
        var release = UpdateService.ParseRelease(Release("v1.2.0"), Installed);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release!.Version);
        Assert.Equal("https://x/app.zip", release.ZipUrl);
    }

    [Theory]
    [InlineData("v1.1.0")]
    [InlineData("v1.0.9")]
    [InlineData("1.1")]
    public void Ignores_same_or_older_releases(string tag)
    {
        Assert.Null(UpdateService.ParseRelease(Release(tag), Installed));
    }

    [Fact]
    public void Two_part_tags_are_compared_correctly()
    {
        Assert.NotNull(UpdateService.ParseRelease(Release("v1.2"), Installed));
    }

    [Fact]
    public void Ignores_pre_release_style_tags_and_missing_zips()
    {
        Assert.Null(UpdateService.ParseRelease(Release("v1.2.0-beta"), Installed));
        Assert.Null(UpdateService.ParseRelease(Release("v9.0.0", withZip: false), Installed));
    }
}

public class FullscreenRuleTests
{
    static AppSettings With(bool hideByDefault, params (string Process, FullscreenRule Rule)[] apps) => new()
    {
        HideInFullscreen = hideByDefault,
        FullscreenApps = apps.Select(a => new FullscreenApp { ProcessName = a.Process, DisplayName = a.Process, Rule = a.Rule }).ToList()
    };

    [Fact]
    public void Default_rule_follows_the_main_switch()
    {
        Assert.True(With(true).ShouldHideFor("game"));
        Assert.False(With(false).ShouldHideFor("game"));
        Assert.True(With(true, ("game", FullscreenRule.Default)).ShouldHideFor("game"));
    }

    [Fact]
    public void Per_app_rules_override_the_main_switch()
    {
        var settings = With(true, ("vlc", FullscreenRule.Show));
        Assert.False(settings.ShouldHideFor("vlc"));
        Assert.True(settings.ShouldHideFor("game"));

        settings = With(false, ("POWERPNT", FullscreenRule.Hide));
        Assert.True(settings.ShouldHideFor("powerpnt"));
        Assert.False(settings.ShouldHideFor("chrome"));
    }
}

public class SpringTests
{
    static void Run(Spring spring, double seconds)
    {
        for (double t = 0; t < seconds; t += 1.0 / 60)
            spring.Step(1.0 / 60);
    }

    [Fact]
    public void Settles_on_the_target()
    {
        var spring = new Spring(38, 0.5, 0.8) { Target = 172 };
        Run(spring, 3);

        Assert.True(spring.IsSettled);
        Assert.Equal(172, spring.Value, 2);
    }

    [Fact]
    public void Bouncy_spring_overshoots_and_critical_does_not()
    {
        var bouncy = new Spring(0, 0.5, 0.5) { Target = 100 };
        var critical = new Spring(0, 0.5, 1) { Target = 100 };
        double bouncyMax = 0, criticalMax = 0;

        for (int i = 0; i < 180; i++)
        {
            bouncy.Step(1.0 / 60);
            critical.Step(1.0 / 60);
            bouncyMax = Math.Max(bouncyMax, bouncy.Value);
            criticalMax = Math.Max(criticalMax, critical.Value);
        }

        Assert.True(bouncyMax > 105);
        Assert.True(criticalMax <= 100.5);
    }

    [Fact]
    public void Retargeting_keeps_momentum()
    {
        var spring = new Spring(0, 0.5, 0.8) { Target = 100 };
        Run(spring, 0.1);
        double velocity = spring.Velocity;

        spring.Target = 0;

        Assert.Equal(velocity, spring.Velocity);
        Assert.True(velocity > 0);
    }

    [Fact]
    public void Large_frame_gaps_stay_stable()
    {
        var spring = new Spring(0, 0.3, 0.7) { Target = 400 };

        for (int i = 0; i < 20; i++)
            spring.Step(0.25);

        Assert.InRange(spring.Value, 399, 401);
    }
}
