using Merchant.Feeds;
using Microsoft.Extensions.Configuration;

namespace Merchant;

/// <summary>
/// Finds, seeds and reads merchant's one settings file.
///
/// There is exactly one file and it is not layered over anything: what it says is what merchant
/// does. Layering a user file over shipped defaults reads well in a design document and behaves
/// badly in practice — configuration merges JSON arrays by index, so a partial override quietly
/// produces a catalog that is neither the default nor what was written.
/// </summary>
public static class MerchantConfig
{
    /// <summary>
    /// Overrides where the settings file is read from. The container points it at the volume.
    /// </summary>
    public const string PathVariable = "MERCHANT_CONFIG";

    /// <summary>The settings file, under a directory of merchant's own.</summary>
    private const string FileName = "appsettings.json";

    /// <summary>Shipped beside the binary and copied into place on a first run.</summary>
    private const string ExampleFileName = "appsettings.example.jsonc";

    /// <summary>The directory merchant keeps its settings and its token file in.</summary>
    private const string DirectoryName = "merchant";

    /// <summary>
    /// The environment variables that configure the same settings as the file, for the unit and the
    /// container that pass them. Named explicitly because they do not spell their settings.
    /// </summary>
    private static readonly (string Variable, string Setting)[] Overrides =
    [
        ("MERCHANT_DB", $"{Schema.Bot}:{Schema.BotKeys.DatabasePath}"),
        ("MERCHANT_SWEEP_MINUTES", $"{Schema.Bot}:{Schema.BotKeys.SweepMinutes}"),
        ("MERCHANT_USER_AGENT", $"{Schema.Bot}:{Schema.BotKeys.UserAgent}"),
        ("MERCHANT_DEV_GUILD", $"{Schema.Bot}:{Schema.BotKeys.DevGuildId}"),
    ];

    /// <summary>
    /// Where the settings file lives: <c>$MERCHANT_CONFIG</c>, else the XDG config directory.
    /// </summary>
    public static string ResolvePath()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } explicitPath)
        {
            return Path.GetFullPath(explicitPath);
        }

        return Path.Combine(BaseDirectory("XDG_CONFIG_HOME", ".config"), DirectoryName, FileName);
    }

    /// <summary>
    /// Where the ledger lives when the settings file names no path of its own: the XDG state
    /// directory, which is where a long-lived file a person never edits belongs.
    ///
    /// Absolute, deliberately. A relative default resolves against the working directory, so the
    /// same install writes a different database depending on where it was started from — one
    /// beside the binary under a unit, one in the checkout under <c>dotnet run</c>, and a bot that
    /// has apparently forgotten every subscription.
    /// </summary>
    public static string ResolveDatabasePath() =>
        Path.Combine(BaseDirectory("XDG_STATE_HOME", Path.Combine(".local", "state")),
            DirectoryName, Schema.Defaults.DatabaseFileName);

    /// <summary>One of the XDG base directories, falling back to its default place under home.</summary>
    private static string BaseDirectory(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);

    /// <summary>
    /// Writes the shipped example to <paramref name="path"/> when nothing is there. A first run
    /// ending in "no configuration found" looks broken, and the person hitting it on a fresh
    /// container has nothing to copy from; an empty volume produces a working bot instead.
    /// </summary>
    /// <returns>What went wrong, or null when there is now a file at that path.</returns>
    public static string? Seed(string path)
    {
        if (File.Exists(path))
        {
            return null;
        }

        string example = Path.Combine(AppContext.BaseDirectory, ExampleFileName);

        if (!File.Exists(example))
        {
            return $"No settings file at {path}, and the example to seed it from " +
                   $"({example}) is missing from this install.";
        }

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(example, path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"No settings file at {path}, and it could not be created: {ex.Message}";
        }
    }

    /// <summary>Reads the file, then lets <see cref="Overrides"/> win over it.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON.</exception>
    public static IConfigurationRoot Load(string path) => new ConfigurationBuilder()
        .AddJsonFile(path, optional: false, reloadOnChange: false)
        .AddInMemoryCollection(Overrides
            .Where(o => Environment.GetEnvironmentVariable(o.Variable) is { Length: > 0 })
            .Select(o => new KeyValuePair<string, string?>(
                o.Setting, Environment.GetEnvironmentVariable(o.Variable))))
        .Build();
}
