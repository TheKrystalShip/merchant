using Microsoft.Extensions.Configuration;

namespace Merchant;

/// <summary>
/// Finds, seeds and reads merchant's one settings file.
///
/// There is exactly one file and it is not layered over anything: what it says is what merchant
/// does. Layering a user file over shipped defaults reads well in a design document and behaves
/// badly in practice — configuration merges JSON arrays by index, so a partial override quietly
/// produces a catalog that is neither the default nor what was written.
///
/// The file is standard <c>appsettings.json</c>, so it takes comments and trailing commas: the
/// example is annotated line by line, which is most of what makes a hand-curated catalog bearable
/// to edit six months later.
/// </summary>
public static class MerchantConfig
{
    /// <summary>Overrides where the settings file is read from. The container sets it to the volume.</summary>
    public const string PathVariable = "MERCHANT_CONFIG";

    /// <summary>Shipped beside the binary and copied into place on first run.</summary>
    private const string ExampleFile = "appsettings.example.jsonc";

    /// <summary>
    /// Where the settings file lives: <c>$MERCHANT_CONFIG</c>, else the XDG config directory.
    /// </summary>
    public static string ResolvePath()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } explicitPath)
        {
            return Path.GetFullPath(explicitPath);
        }

        string home = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(home, "merchant", "appsettings.json");
    }

    /// <summary>
    /// Writes the shipped example to <paramref name="path"/> when nothing is there yet.
    ///
    /// A first run that ends in "no configuration found" is a first run that looks broken, and the
    /// person hitting it on a fresh container has nothing to copy from. Seeding means an empty
    /// volume still produces a working bot, and the file it produces is the one to edit.
    /// </summary>
    /// <returns>What went wrong, or null when there is now a file at that path.</returns>
    public static string? Seed(string path)
    {
        if (File.Exists(path))
        {
            return null;
        }

        string example = Path.Combine(AppContext.BaseDirectory, ExampleFile);

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

    /// <summary>
    /// Reads the file, then lets the environment override it.
    ///
    /// The old variable names are mapped explicitly rather than through the prefixed environment
    /// provider, because <c>MERCHANT_DB</c> and friends do not spell the settings they set. They
    /// are named in the unit file, the container and the README, so they keep working.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON.</exception>
    public static IConfigurationRoot Load(string path) => new ConfigurationBuilder()
        .AddJsonFile(path, optional: false, reloadOnChange: false)
        .AddInMemoryCollection(LegacyEnvironment())
        .Build();

    /// <summary>The environment variables that predate the settings file, as settings.</summary>
    private static IEnumerable<KeyValuePair<string, string?>> LegacyEnvironment()
    {
        (string Variable, string Setting)[] mapping =
        [
            ("MERCHANT_DB", "bot:databasePath"),
            ("MERCHANT_SWEEP_MINUTES", "bot:sweepMinutes"),
            ("MERCHANT_USER_AGENT", "bot:userAgent"),
            ("MERCHANT_DEV_GUILD", "bot:devGuildId"),
        ];

        return mapping
            .Where(m => Environment.GetEnvironmentVariable(m.Variable) is { Length: > 0 })
            .Select(m => new KeyValuePair<string, string?>(
                m.Setting, Environment.GetEnvironmentVariable(m.Variable)));
    }
}
