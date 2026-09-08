using Microsoft.Extensions.Configuration;

namespace Merchant.Feeds;

/// <summary>
/// Every driver merchant knows how to build, looked up by the <c>type</c> a feed names.
///
/// This replaces the switch the catalog used to carry. A new driver registers itself in the
/// container and appears here; nothing else in the codebase learns its name.
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

    /// <summary>
    /// Builds the blueprint for one feed's <c>source</c> block, or reports why it could not.
    /// </summary>
    public ISourceBlueprint? Create(IConfigurationSection source, ICollection<string> errors)
    {
        if (!source.Exists())
        {
            errors.Add($"source is missing — it needs at least a type ({string.Join(" or ", Types)}).");
            return null;
        }

        if (ConfigRead.Optional(source, "type") is not { } type)
        {
            errors.Add($"source.type is missing — it should be {string.Join(" or ", Types)}.");
            return null;
        }

        if (!_factories.TryGetValue(type, out ISourceFactory? factory))
        {
            errors.Add($"source.type '{type}' is not a kind of source merchant knows — " +
                       $"it can read {string.Join(" and ", Types)}.");
            return null;
        }

        return factory.Create(source, errors);
    }
}
