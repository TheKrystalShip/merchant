using Discord.Interactions;
using Merchant.Feeds;

namespace Merchant.Discord;

/// <summary>How often the channel hears from merchant, with a default that suits the feed.</summary>
public enum CadenceChoice
{
    /// <summary>Whatever the feed is best suited to. Almost always the right answer.</summary>
    [ChoiceDisplay("Recommended for this feed")]
    Default,

    /// <summary>Each item as it turns up.</summary>
    [ChoiceDisplay("Every item, as it appears")]
    Live,

    /// <summary>One digest a day.</summary>
    [ChoiceDisplay("One post a day")]
    Daily,

    /// <summary>One digest a week.</summary>
    [ChoiceDisplay("One post a week")]
    Weekly,
}

/// <summary>Translates the command menus into catalog and domain values.</summary>
public static class Choices
{
    /// <summary>The cadence to store, resolving <see cref="CadenceChoice.Default"/> against the feed.</summary>
    public static Cadence Resolve(this CadenceChoice choice, Category category) => choice switch
    {
        CadenceChoice.Default => category.DefaultCadence,
        CadenceChoice.Live => Cadence.Live,
        CadenceChoice.Daily => Cadence.Daily,
        CadenceChoice.Weekly => Cadence.Weekly,
        _ => category.DefaultCadence,
    };

    /// <summary>How a cadence reads in a confirmation message.</summary>
    public static string Describe(this Cadence cadence) => cadence switch
    {
        Cadence.Live => "as they appear",
        Cadence.Daily => "once a day",
        Cadence.Weekly => "once a week",
        _ => cadence.ToString(),
    };
}
