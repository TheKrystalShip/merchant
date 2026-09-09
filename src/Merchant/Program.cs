using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Merchant.Discord;
using Merchant.Feeds;
using Merchant.Feeds.Factories;
using Merchant.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Merchant;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Answered before the settings file is opened, because both are questions about this
        // program rather than about a configured one: --help is what somebody types when nothing
        // works yet, and --version is what they are asked for when something stopped working.
        if (args is ["--help" or "-h", ..])
        {
            Console.Out.Write(Usage());
            return 0;
        }

        if (args is ["--version", ..])
        {
            Console.Out.WriteLine($"merchant {Build.FullVersion}");
            return 0;
        }

        // Everything merchant posts is described by one settings file, so reading it is the first
        // thing that happens on either path — the bot's and --check's. A missing file is seeded
        // rather than refused: a fresh container with an empty volume should come up working and
        // leave behind the file to edit.
        string configPath = MerchantConfig.ResolvePath();

        if (MerchantConfig.Seed(configPath) is { } seedProblem)
        {
            Console.Error.WriteLine(seedProblem);
            return 1;
        }

        IConfigurationRoot config;

        try
        {
            config = MerchantConfig.Load(configPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException
                                      or UnauthorizedAccessException or FormatException)
        {
            // The configuration provider wraps the real complaint twice over: the outer message names
            // the file and nothing else, and the line and column that locate a stray comma are on the
            // reader's own exception at the bottom of the chain. Somebody is looking at this file in
            // an editor, so the innermost one is said as well as the first.
            Exception cause = ex.GetBaseException();
            string detail = ReferenceEquals(cause, ex) ? ex.Message : $"{ex.Message} {cause.Message}";

            Console.Error.WriteLine($"Could not read {configPath}: {detail}");
            return 1;
        }

        List<string> problems = [];
        BotOptions options = BotOptions.Load(config, problems);

        SourceRegistry registry = new([new RssSourceFactory(), new CheapSharkSourceFactory()]);
        FeedCatalog catalog = FeedCatalog.Load(
            config.GetSection(Schema.Feeds), registry, out CatalogReport report);

        // Said once, on stderr, before any logger exists — this is the output somebody stares at
        // after editing the file, and every line names the feed and what to fix.
        Console.Error.WriteLine($"Settings: {configPath}");

        foreach (string problem in problems.Concat(report.Errors))
        {
            Console.Error.WriteLine($"  ! {problem}");
        }

        Console.Error.WriteLine($"  {report.Summary}");

        if (catalog.All.Count == 0)
        {
            Console.Error.WriteLine(
                "No usable feeds, so there is nothing merchant could announce. Fix the errors above, " +
                $"or delete {configPath} to have the shipped example written back.");
            return 1;
        }

        // The feed check needs no token and no gateway, so it runs before anything else is configured.
        if (args is ["--check", ..])
        {
            // The region a feed is checked for. Left out, it is the one a server has before anybody
            // runs /merchant region, which is the answer most people are asking about.
            string region = GuildSettings.Default(0).Region;

            if (args is [_, { } asked, ..])
            {
                if (!GuildSettings.IsRegion(asked))
                {
                    Console.Error.WriteLine(
                        $"'{asked}' is not a country code. Give two letters, like ES or GB, or leave " +
                        $"it out to check the feeds as they arrive in {region}.");
                    return 1;
                }

                region = asked.ToUpperInvariant();
            }

            return await Preflight.RunAsync(catalog, options.UserAgent, region, CancellationToken.None);
        }

        // Anything else that reads like a command is a mistake worth naming: the host would otherwise
        // take it as configuration, ignore it, and go on to demand a token for a run nobody asked for.
        if (args is [{ } first, ..] && !first.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"merchant has no command '{first}'. It takes --check [COUNTRY] to read the " +
                "settings file and fetch every feed, and no argument at all to run the bot. " +
                "--help lists the lot.");
            return 1;
        }

        string? token = Environment.GetEnvironmentVariable(BotOptions.TokenVariable);

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                $"{BotOptions.TokenVariable} is not set. Create an application at " +
                "https://discord.com/developers, add a bot to it, and put its token in " +
                $"{BotOptions.TokenVariable}.");
            return 1;
        }

        // Opened here rather than by the container, so a database this build cannot read is refused in
        // a sentence beside the other startup refusals instead of surfacing as a stack trace out of a
        // hosted service an hour later. Handed over as an instance, which makes disposing it this
        // file's job: the using below runs after the host's, so the sweep has stopped before the
        // connection closes.
        if (Ledger.Open(options.DatabasePath, out string? ledgerProblem) is not { } opened)
        {
            Console.Error.WriteLine(ledgerProblem);
            return 1;
        }

        using Ledger ledger = opened;

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // Added last so merchant's own file wins over anything the host picked up beside the binary.
        builder.Configuration.AddConfiguration(config);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton(ledger);

        // A driver is a class and a line here. Nothing else in the codebase names one, which is what
        // keeps the catalog in the settings file rather than spread across a switch and an enum.
        builder.Services.AddSingleton<ISourceFactory, RssSourceFactory>();
        builder.Services.AddSingleton<ISourceFactory, CheapSharkSourceFactory>();
        builder.Services.AddSingleton<SourceRegistry>();

        // Every outbound fetch goes through this one client, so the user agent is impossible to
        // forget. CheapShark refuses a request without a descriptive one, and Reddit throttles it
        // harder.
        builder.Services.AddHttpClient(BotOptions.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });

        builder.Services.AddSingleton(new DiscordSocketConfig
        {
            // Feeds are pushed, never read: merchant needs no message content and no member list,
            // which keeps it off the privileged intents entirely and makes it approvable without
            // review.
            GatewayIntents = GatewayIntents.Guilds,
            AlwaysDownloadUsers = false,
            LogGatewayIntentWarnings = false,
        });

        builder.Services.AddSingleton<DiscordSocketClient>();
        builder.Services.AddSingleton(s => new InteractionService(
            s.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async, UseCompiledLambda = true }));

        builder.Services.AddHostedService<Sweeper>();

        using IHost host = builder.Build();

        DiscordSocketClient discord = host.Services.GetRequiredService<DiscordSocketClient>();
        InteractionService interactions = host.Services.GetRequiredService<InteractionService>();
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("merchant");

        // A refused token is the one failure that reconnecting cannot mend, and it is also the most
        // likely mistake somebody setting this up will make. Discord.Net treats it like a dropped
        // connection and retries for as long as the process lives, so without this merchant reports
        // itself up, posts nothing, and says why only inside a stack trace. Said once, plainly, and
        // then stop.
        bool unauthorized = false;
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        discord.Log += message =>
        {
            if (!unauthorized && GatewayFailure.IsUnauthorized(message.Exception))
            {
                unauthorized = true;

                logger.LogCritical(
                    "Discord refused {Variable} (401 Unauthorized). The token is wrong, or it was " +
                    "reset — resetting one at https://discord.com/developers invalidates the old " +
                    "value. merchant is stopping rather than retrying a token that cannot start " +
                    "working.",
                    BotOptions.TokenVariable);

                lifetime.StopApplication();
            }

            return Relay(logger, message);
        };

        interactions.Log += message => Relay(logger, message);

        await interactions.AddModuleAsync<MerchantModule>(host.Services);

        discord.InteractionCreated += async interaction =>
        {
            SocketInteractionContext context = new(discord, interaction);
            await interactions.ExecuteCommandAsync(context, host.Services);
        };

        discord.Ready += async () =>
        {
            // Guild-scoped commands appear at once where global ones take up to an hour to propagate,
            // so a dev guild is the difference between testing a change now and testing it after
            // lunch.
            if (options.DevGuildId is { } guild)
            {
                await interactions.RegisterCommandsToGuildAsync(guild);
                logger.LogInformation("Commands registered to guild {Guild}.", guild);
            }
            else
            {
                await interactions.RegisterCommandsGloballyAsync();
                logger.LogInformation("Commands registered globally.");
            }

            // The feed list is not registered with Discord — it is resolved per keystroke by the
            // autocomplete handler — so editing the settings file and restarting is the whole of it.
            logger.LogInformation(
                "merchant {Version} is up as {User}, {Feeds} feed(s), sweeping every {Minutes} " +
                "minute(s), ledger at {Path}.",
                Build.FullVersion,
                discord.CurrentUser?.Username ?? "?",
                catalog.All.Count,
                options.SweepInterval.TotalMinutes,
                options.DatabasePath);
        };

        await discord.LoginAsync(TokenType.Bot, token);
        await discord.StartAsync();

        await host.StartAsync();
        await host.WaitForShutdownAsync();

        // Started and waited on rather than RunAsync, which disposes the host the moment it returns —
        // and the host owns the gateway client, so the logout below would be talking to a disposed
        // object.
        //
        // Shutdown is SIGTERM, which is a restart or a deploy nine times out of ten. Closing the
        // session says so: the bot shows offline at once instead of hanging around until the gateway
        // times it out. The host is disposed by the using above, after this, and the ledger after
        // that.
        await discord.StopAsync();
        await discord.LogoutAsync();

        // Non-zero, so a refused token reads as a failed unit and a restarting container rather than
        // as a service that decided to stop. Every other shutdown here is somebody asking for one.
        return unauthorized ? 1 : 0;
    }

    /// <summary>
    /// Everything merchant can be told, and everything it reads from the environment. The paths
    /// are resolved rather than described, because "the XDG config directory" is not an answer to
    /// somebody trying to find the file.
    /// </summary>
    private static string Usage() =>
        $"""
        merchant {Build.Version} — game-deal announcements for Discord.

        Usage:
          merchant                      run the bot; needs {BotOptions.TokenVariable} in the environment
          merchant --check [COUNTRY]    read the settings file and fetch every feed, without Discord
          merchant --version            the version this build reports, and the commit it came from
          merchant --help               this

        Files:
          {MerchantConfig.ResolvePath()}
              the settings file: every feed merchant can post, and every setting. Edit and restart.
          {MerchantConfig.ResolveDatabasePath()}
              the ledger: which channels are subscribed, and what each has already been shown.
              A databasePath in the settings file puts it somewhere else.

        Environment:
          {BotOptions.TokenVariable}          the bot token, and the only way it is ever given.
                                  https://discord.com/developers/applications
          {MerchantConfig.PathVariable}         where the settings file is read from.
          {string.Join(", ", MerchantConfig.OverrideVariables)}
                                  override the matching setting in the file.

        The feeds are data, not code: adding, changing or removing one is an edit to the settings
        file and a restart. https://github.com/TheKrystalShip/merchant

        """;

    // Discord.Net has its own severity ladder; this maps it onto the host's logger so there is one
    // log stream to read in journalctl rather than two interleaved formats.
    private static Task Relay(ILogger logger, LogMessage message)
    {
        LogLevel level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

#pragma warning disable CA2254 // The source's own message is the template; there are no arguments.
        logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
#pragma warning restore CA2254

        return Task.CompletedTask;
    }
}
