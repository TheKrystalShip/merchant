using Merchant.Feeds;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Merchant.Discord;

/// <summary>
/// The feed menu, resolved when somebody opens it.
///
/// Discord is told a command's fixed choices once, at registration, so a catalog living in a file
/// could only be shown by re-registering commands on every edit. Autocomplete is asked per
/// keystroke instead — including the empty one, so the full list appears the moment the field is
/// focused, which is what makes this read as a menu rather than a text box.
/// </summary>
public sealed class FeedAutocomplete : AutocompleteHandler
{
    /// <summary>Discord will not accept more suggestions than this in one response.</summary>
    private const int Limit = 25;

    /// <inheritdoc />
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        FeedCatalog catalog = services.GetRequiredService<FeedCatalog>();

        return Task.FromResult(AutocompletionResult.FromSuccess(
            Suggest(catalog, interaction.Data.Current.Value as string ?? string.Empty)));
    }

    /// <summary>
    /// What the menu offers for what has been typed so far. Separate from the interaction so it can
    /// be tested against a real catalog rather than against a mocked gateway payload.
    /// </summary>
    internal static IReadOnlyList<AutocompleteResult> Suggest(FeedCatalog catalog, string typed) =>
        [.. catalog.All
            .Where(category => Matches(category, typed.Trim()))
            .Take(Limit)
            .Select(category => new AutocompleteResult(category.Label, category.Key))];

    /// <summary>
    /// Matches on the label and on the key, because the key is what somebody who has read the
    /// settings file will type, and the label is what everybody else will.
    /// </summary>
    private static bool Matches(Category category, string typed) =>
        typed.Length == 0
        || category.Label.Contains(typed, StringComparison.OrdinalIgnoreCase)
        || category.Key.Contains(typed, StringComparison.OrdinalIgnoreCase);
}
