using Discord;
using Merchant.Discord;
using Merchant.Feeds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Merchant.Tests;

/// <summary>
/// The settings file as an artefact rather than as a catalog: where it is found, what happens when
/// it is not there, and what the non-feed half of it configures.
/// </summary>
public class SettingsFileTests
{
    [Fact]
    public void A_missing_file_is_written_rather_than_refused()
    {
        // A first run that ends in "no configuration found" looks broken, and on a fresh container
        // there is nothing to copy from. Seeding means an empty volume still produces a bot.
        string directory = Path.Combine(Path.GetTempPath(), $"merchant-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            Assert.Null(MerchantConfig.Seed(path));
            Assert.True(File.Exists(path));

            FeedCatalog seeded = FeedCatalog.Load(
                MerchantConfig.Load(path).GetSection("feeds"), Settings.Registry, out CatalogReport report);

            Assert.Empty(report.Errors);
            Assert.Equal(5, seeded.All.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void An_existing_file_is_never_overwritten()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"merchant-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{ }");

            Assert.Null(MerchantConfig.Seed(path));
            Assert.Equal("{ }", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_path_can_be_pointed_somewhere_else()
    {
        // The container has no home directory to speak of; it points this at its volume.
        Assert.EndsWith("appsettings.json", MerchantConfig.ResolvePath(), StringComparison.Ordinal);
        Assert.Contains("merchant", MerchantConfig.ResolvePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_bot_section_falls_back_to_working_defaults()
    {
        List<string> problems = [];
        BotOptions options = BotOptions.Load(Settings.Read("""{ "feeds": { } }"""), problems);

        Assert.Empty(problems);
        Assert.Equal(MerchantConfig.ResolveDatabasePath(), options.DatabasePath);
        Assert.Equal(TimeSpan.FromMinutes(30), options.SweepInterval);
        Assert.Contains("merchant", options.UserAgent, StringComparison.Ordinal);

        // The version reaches the wire, so a feed host's logs can tell one build from another.
        Assert.Contains(Build.Version, options.UserAgent, StringComparison.Ordinal);
        Assert.Null(options.DevGuildId);
    }

    [Fact]
    public void The_bot_section_is_read_when_it_is_there()
    {
        List<string> problems = [];
        BotOptions options = BotOptions.Load(Settings.Read("""
            {
              "bot": {
                "databasePath": "/data/merchant.db",
                "sweepMinutes": 15,
                "userAgent": "merchant/test",
                "devGuildId": 123456789012345678
              }
            }
            """), problems);

        Assert.Empty(problems);
        Assert.Equal("/data/merchant.db", options.DatabasePath);
        Assert.Equal(TimeSpan.FromMinutes(15), options.SweepInterval);
        Assert.Equal("merchant/test", options.UserAgent);
        Assert.Equal(123456789012345678UL, options.DevGuildId);
    }

    [Theory]
    [InlineData("""{ "bot": { "sweepMinutes": 1 } }""", "sweepMinutes")]
    [InlineData("""{ "bot": { "sweepMinutes": "often" } }""", "sweepMinutes")]
    [InlineData("""{ "bot": { "devGuildId": "my server" } }""", "devGuildId")]
    public void A_bad_bot_setting_is_reported_and_the_default_stands(string json, string expected)
    {
        List<string> problems = [];
        BotOptions options = BotOptions.Load(Settings.Read(json), problems);

        Assert.Contains(expected, Assert.Single(problems), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(30), options.SweepInterval);
    }

    [Fact]
    public void A_setting_that_does_not_exist_is_reported_rather_than_ignored()
    {
        List<string> problems = [];
        BotOptions.Load(Settings.Read("""{ "bot": { "sweepMinutes ": 30, "userAgentt": "x" } }"""), problems);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Contains("there is no setting", p, StringComparison.Ordinal));
    }

    [Fact]
    public void A_token_in_the_file_is_called_out_rather_than_used()
    {
        // The token is the one secret merchant holds. This file gets copied, pasted and shared;
        // MERCHANT_TOKEN does not.
        List<string> problems = [];
        BotOptions.Load(Settings.Read("""{ "bot": { "token": "definitely.a.token" } }"""), problems);

        Assert.Contains(BotOptions.TokenVariable, Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void The_environment_still_overrides_the_file()
    {
        // The unit file and the container name these, and both predate the settings file.
        string path = Path.Combine(Path.GetTempPath(), $"merchant-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """{ "bot": { "databasePath": "from-file.db" } }""");
            Environment.SetEnvironmentVariable("MERCHANT_DB", "from-environment.db");

            List<string> problems = [];

            Assert.Equal("from-environment.db",
                BotOptions.Load(MerchantConfig.Load(path), problems).DatabasePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERCHANT_DB", null);
            File.Delete(path);
        }
    }

    [Fact]
    public void The_ledger_defaults_to_an_absolute_path_of_its_own()
    {
        // A relative default resolves against the working directory, which means a bot started
        // from a different place quietly reads a different database and looks freshly installed.
        string path = MerchantConfig.ResolveDatabasePath();

        Assert.True(Path.IsPathRooted(path));
        Assert.EndsWith(Schema.Defaults.DatabaseFileName, path, StringComparison.Ordinal);
        Assert.Contains("merchant", path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "feed": { }, "bot": { } }""", "feed")]
    [InlineData("""{ "feeds": { }, "settings": { } }""", "settings")]
    public void A_section_that_is_not_in_the_schema_is_named(string json, string expected)
    {
        // The root is held to the schema like every level below it. A catalog under "feed" starts
        // a bot that announces nothing, and the reason is a single letter nothing else would mention.
        List<string> problems = [];
        BotOptions.Load(Settings.Read(json), problems);

        Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void The_sections_the_host_reads_are_not_reported_as_mistakes()
    {
        List<string> problems = [];
        BotOptions.Load(Settings.Read("""
            {
              "bot": { },
              "feeds": { },
              "logging": { "logLevel": { "default": "Debug" } }
            }
            """), problems);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("""{ }""", false)]
    [InlineData("""{ "logging": { "logLevel": { "default": "Debug" } } }""", true)]
    public void The_log_level_is_turned_up_from_the_settings_file(string json, bool verbose)
    {
        // Merchant reads no logging setting of its own: it hands the host its file and the host's
        // own section does the rest. This pins that the wiring in Program.cs actually delivers it,
        // because "edit this to see more" is the first thing anybody debugging is told.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddConfiguration(Settings.Read(json));

        using IHost host = builder.Build();
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("merchant");

        Assert.Equal(verbose, logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));
    }
}

/// <summary>
/// The menu, which is resolved per keystroke now rather than registered with Discord up front.
/// </summary>
public class FeedAutocompleteTests
{
    [Fact]
    public void An_empty_box_offers_the_whole_catalog()
    {
        // This is what makes autocomplete read as a menu: focus the field and the list is there,
        // without having guessed a letter first.
        FeedCatalog catalog = Settings.Example(out _);

        Assert.Equal(catalog.All.Count, FeedAutocomplete.Suggest(catalog, "").Count);
    }

    [Theory]
    [InlineData("free", "free-games")]
    [InlineData("FREE", "free-games")]
    [InlineData("under", "under-10")]
    // The key is what somebody who has read the settings file will type.
    [InlineData("top-week", "top-week")]
    public void Typing_narrows_it(string typed, string expected)
    {
        AutocompleteResult match = Assert.Single(FeedAutocomplete.Suggest(Settings.Example(out _), typed));

        Assert.Equal(expected, match.Value);
    }

    [Fact]
    public void A_name_that_matches_nothing_offers_nothing()
    {
        Assert.Empty(FeedAutocomplete.Suggest(Settings.Example(out _), "kingdom hearts"));
    }

    [Fact]
    public void The_menu_never_exceeds_what_Discord_accepts()
    {
        // Discord rejects the whole response past 25, which would take the menu down for every
        // feed at once rather than just the ones past the cap.
        string feeds = string.Join(",", Enumerable.Range(0, 40).Select(i =>
            $$"""
              "feed-{{i}}": { "label": "Feed {{i}}", "description": "D",
                "source": { "type": "rss", "urls": [ "https://example.test/{{i}}" ] } }
              """));

        FeedCatalog many = Settings.From($$"""{ "feeds": { {{feeds}} } }""", out CatalogReport report);

        Assert.Equal(40, report.Loaded);
        Assert.Equal(25, FeedAutocomplete.Suggest(many, "").Count);
    }
}
