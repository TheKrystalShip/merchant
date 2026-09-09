using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Merchant.Sources;

/// <summary>
/// Reads one or more syndication feeds and flattens them into <see cref="FeedItem"/>s.
///
/// It is deliberately namespace-blind. The three feeds merchant ships against are three different
/// formats — Steam's chart is RSS 1.0, where items are siblings of <c>&lt;channel&gt;</c> under an
/// RDF root and carry no <c>guid</c>; IsThereAnyDeal is ordinary RSS 2.0; Reddit is Atom. Matching
/// on local names covers all three without a format flag per feed.
/// </summary>
public sealed partial class RssSource : ISource
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _urls;

    /// <summary>The feeds this source reads, with any placeholders already filled in.</summary>
    internal IReadOnlyList<string> Urls => _urls;

    /// <summary>Reads the given feed URLs, merging their items into one list.</summary>
    public RssSource(HttpClient http, IReadOnlyList<string> urls)
    {
        _http = http;
        _urls = urls;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct)
    {
        List<FeedItem> items = [];

        foreach (string url in _urls)
        {
            try
            {
                string xml = await _http.GetStringAsync(url, ct);
                items.AddRange(Parse(xml));
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A feed that is down, rate-limiting or serving junk costs its own items and
                // nothing else. Reddit in particular answers 429 often enough that treating it
                // as fatal would silence the whole category.
            }
        }

        return [.. items.OrderByDescending(i => i.Published ?? DateTimeOffset.MinValue)];
    }

    /// <summary>Flattens a feed document. Public so the parser can be tested without a network.</summary>
    internal static IReadOnlyList<FeedItem> Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        List<FeedItem> items = [];

        foreach (XElement entry in doc.Descendants().Where(e =>
                     e.Name.LocalName is "item" or "entry"))
        {
            string? link = ReadLink(entry);
            string? title = Clean(Child(entry, "title"));
            if (link is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            string id = Child(entry, "guid")
                        ?? Child(entry, "id")
                        ?? entry.Attributes().FirstOrDefault(a => a.Name.LocalName == "about")?.Value
                        ?? link;

            string? body = Child(entry, "encoded")      // content:encoded
                           ?? Child(entry, "content")
                           ?? Child(entry, "description")
                           ?? Child(entry, "summary");

            items.Add(new FeedItem(
                Id: id.Trim(),
                Title: title!,
                Url: link,
                Summary: Summarise(body),
                ImageUrl: FirstImage(body),
                Published: ReadDate(entry)));
        }

        return items;
    }

    /// <summary>
    /// The entry's destination. Atom puts it in an <c>href</c> attribute and may carry several
    /// links, only one of which is the article; RSS puts it in the element's text. Anything that is
    /// not an absolute http(s) address costs the entry — see <see cref="Links"/>.
    /// </summary>
    private static string? ReadLink(XElement entry)
    {
        foreach (XElement link in entry.Elements().Where(e => e.Name.LocalName == "link"))
        {
            string? href = link.Attribute("href")?.Value;
            if (href is not null)
            {
                string? rel = link.Attribute("rel")?.Value;
                if (rel is null or "alternate")
                {
                    return Links.Http(href);
                }

                continue;
            }

            if (Links.Http(link.Value) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The text of the first child with this local name, or null when there is none.</summary>
    private static string? Child(XElement parent, string localName)
    {
        XElement? found = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        return string.IsNullOrWhiteSpace(found?.Value) ? null : found.Value;
    }

    /// <summary>
    /// The entry's timestamp, across the several element names the formats use for it.
    ///
    /// Read against the invariant culture, never the host's. A feed writes English month and day
    /// names on a Gregorian calendar whatever locale the machine reading it happens to have, and
    /// merchant runs with globalization on: under a culture whose default calendar is not Gregorian
    /// — fa-IR, th-TH — the same string is refused or lands six centuries out, which silently
    /// reorders every digest it appears in.
    /// </summary>
    private static DateTimeOffset? ReadDate(XElement entry)
    {
        foreach (string name in (string[])["pubDate", "published", "date", "updated"])
        {
            string? raw = Child(entry, name);
            if (raw is not null && DateTimeOffset.TryParse(
                    raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset when))
            {
                return when;
            }
        }

        return null;
    }

    /// <summary>
    /// A sentence of plain text from a body that is usually HTML. Discord renders neither tags
    /// nor entities, so both come out here rather than in the announcer.
    /// </summary>
    private static string? Summarise(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        string text = Clean(TagPattern().Replace(html, " ")) ?? string.Empty;
        text = WhitespacePattern().Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return null;
        }

        const int limit = 280;
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit).TrimEnd(), "…");
    }

    /// <summary>
    /// The first image in an entry's HTML body, which is where Steam hides its capsule art.
    /// </summary>
    private static string? FirstImage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        Match match = ImagePattern().Match(html);
        return match.Success ? Links.Http(WebUtility.HtmlDecode(match.Groups[1].Value)) : null;
    }

    /// <summary>
    /// Decodes entities and trims. Feeds double-encode often enough to be worth two passes.
    /// </summary>
    private static string? Clean(string? raw) =>
        raw is null ? null : WebUtility.HtmlDecode(WebUtility.HtmlDecode(raw)).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"<img[^>]+src\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ImagePattern();
}
