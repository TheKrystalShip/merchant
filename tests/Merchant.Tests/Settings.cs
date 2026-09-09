using System.Text;
using Merchant.Feeds;
using Merchant.Feeds.Factories;
using Microsoft.Extensions.Configuration;

namespace Merchant.Tests;

/// <summary>Builds catalogs the way merchant does, from a settings document.</summary>
internal static class Settings
{
    /// <summary>The drivers merchant registers in its container.</summary>
    public static SourceRegistry Registry => new([new RssSourceFactory(), new CheapSharkSourceFactory()]);

    /// <summary>
    /// The shipped example, found beside the test assembly or, when the build did not copy it
    /// there, in the repository it came from.
    /// </summary>
    public static string ExamplePath
    {
        get
        {
            string local = Path.Combine(AppContext.BaseDirectory, "appsettings.example.jsonc");

            if (File.Exists(local))
            {
                return local;
            }

            for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
            {
                string candidate = Path.Combine(at.FullName, "deploy", "appsettings.example.jsonc");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not find appsettings.example.jsonc.");
        }
    }

    /// <summary>The catalog the shipped example describes.</summary>
    public static FeedCatalog Example(out CatalogReport report) =>
        FeedCatalog.Load(
            new ConfigurationBuilder().AddJsonFile(ExamplePath).Build().GetSection(Schema.Feeds),
            Registry,
            out report);

    /// <summary>The catalog a settings document describes.</summary>
    public static FeedCatalog From(string json, out CatalogReport report) =>
        FeedCatalog.Load(Read(json).GetSection(Schema.Feeds), Registry, out report);

    /// <summary>Reads a settings document without writing it to disk first.</summary>
    public static IConfigurationRoot Read(string json) => new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
        .Build();

    /// <summary>One feed object, wrapped in the document that would carry it.</summary>
    public static string Feed(string body) =>
        $$"""{ "{{Schema.Feeds}}": { "example": """ + body + " } }";
}
