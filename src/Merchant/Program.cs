using Merchant;
using Merchant.Discord;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The feed check needs no token and no gateway, so it runs before anything is configured.
if (args is [_, ..] && args[0] is "--check" or "check")
{
    string region = args.Length > 1 ? args[1] : "US";
    return await Preflight.RunAsync(region, CancellationToken.None);
}

BotOptions options;

try
{
    options = BotOptions.FromEnvironment();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new Merchant.Store.Store(options.DatabasePath));

// Every outbound fetch goes through this one client, so the user agent is impossible to forget.
// CheapShark refuses a request without a descriptive one, and Reddit throttles it harder.
builder.Services.AddHttpClient(BotOptions.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});

builder.Services.AddSingleton(new DiscordSocketConfig
{
    // Feeds are pushed, never read: merchant needs no message content and no member list, which keeps
    // it off the privileged intents entirely and makes it approvable without review.
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

discord.Log += message => Relay(logger, message);
interactions.Log += message => Relay(logger, message);

await interactions.AddModuleAsync<MerchantModule>(host.Services);

discord.InteractionCreated += async interaction =>
{
    SocketInteractionContext context = new(discord, interaction);
    await interactions.ExecuteCommandAsync(context, host.Services);
};

discord.Ready += async () =>
{
    // Guild-scoped commands appear at once where global ones take up to an hour to propagate, so a
    // dev guild is the difference between testing a change now and testing it after lunch.
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

    logger.LogInformation(
        "merchant is up as {User}, sweeping every {Minutes} minute(s), ledger at {Path}.",
        discord.CurrentUser?.Username ?? "?",
        options.SweepInterval.TotalMinutes,
        options.DatabasePath);
};

await discord.LoginAsync(TokenType.Bot, options.Token);
await discord.StartAsync();

await host.RunAsync();
return 0;

// Discord.Net has its own severity ladder; this maps it onto the host's logger so there is one
// log stream to read in journalctl rather than two interleaved formats.
static Task Relay(ILogger logger, LogMessage message)
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
