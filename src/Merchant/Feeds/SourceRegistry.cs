using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Every driver merchant can build, looked up by the <see cref="Schema.SourceKeys.Type"/> a feed
/// names. A driver registers itself in the container and appears here; nothing else in the codebase
/// learns its name.
/// </summary>
public sealed class SourceRegistry
{
    private readonly Dictionary<string, ISourceFactory> _factories;

    /// <summary>Indexes the registered factories.</summary>
    /// <exception cref="InvalidOperationException">Two factories claim the same type.</exception>
    public SourceRegistry(IEnumerable<ISourceFactory> factories)
    {
        _factories = new Dictionary<string, ISourceFactory>(StringComparer.OrdinalIgnoreCase);

        foreach (ISourceFactory factory in factories)
        {
            if (!_factories.TryAdd(factory.Type, factory))
            {
                throw new InvalidOperationException(
                    $"Two source factories both claim the type '{factory.Type}'.");
            }
        }
    }

    /// <summary>The registered type names, as they are offered in an error message.</summary>
    public IReadOnlyList<string> Types => [.. _factories.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Builds the blueprint for one feed's source block, or reports why it could not.</summary>
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        string field = Schema.FeedKeys.Source;

        if (!source.Exists())
        {
            errors.Add($"{field} is missing — it needs at least a " +
                       $"{Schema.SourceKeys.Type} ({string.Join(" or ", Types)}).");
            return null;
        }

        if (ConfigRead.Optional(source, Schema.SourceKeys.Type) is not { } type)
        {
            errors.Add($"{field}.{Schema.SourceKeys.Type} is missing — " +
                       $"it should be {string.Join(" or ", Types)}.");
            return null;
        }

        if (!_factories.TryGetValue(type, out ISourceFactory? factory))
        {
            errors.Add($"{field}.{Schema.SourceKeys.Type} '{type}' is not a kind of source merchant " +
                       $"knows — it can read {string.Join(" and ", Types)}.");
            return null;
        }

        int unknown = ConfigRead.Unknown(
            source, [Schema.SourceKeys.Type, .. factory.Keys], $"the {factory.Type} source", errors);

        List<string> mine = [];
        ISourceBlueprint? blueprint = factory.Create(source, mine);

        foreach (string problem in mine)
        {
            errors.Add($"{field}.{problem}");
        }

        return unknown == 0 && mine.Count == 0 ? blueprint : null;
    }
}
