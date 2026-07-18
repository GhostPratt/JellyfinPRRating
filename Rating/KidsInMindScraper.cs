using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Scrapes Kids in Mind ratings (ported from PlexRating's kidsinmind.py).
/// </summary>
public class KidsInMindScraper : ScraperBase
{
    private const string Base = "https://kids-in-mind.com";

    private static readonly Regex _titleTagRegex = new("<title>(.*?)</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex _tripletRegex = new(@"(\d{1,2})\.(\d{1,2})\.(\d{1,2})", RegexOptions.Compiled);
    private static readonly Regex _anchorRegex = new("<a\\s[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex _sexBodyRegex = new(@"(?:SEX|Sex)[/&\s]+(?:NUDITY|Nudity)\s*[:\-]?\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex _violenceBodyRegex = new(@"(?:VIOLENCE|Violence)[/&\s]+(?:GORE|Gore)\s*[:\-]?\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex _languageBodyRegex = new(@"(?:PROFANITY|LANGUAGE|Language|Profanity)\s*[:\-]?\s*(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="KidsInMindScraper"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public KidsInMindScraper(IHttpClientFactory httpClientFactory, ILogger<KidsInMindScraper> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Scrapes Kids in Mind for the given movie.
    /// </summary>
    /// <param name="title">The movie title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scraped data, or <c>null</c> when not found.</returns>
    public async Task<KidsInMindData?> ScrapeAsync(string title, int? year, CancellationToken cancellationToken)
    {
        using var client = CreateClient();

        var url = await FindReviewUrlAsync(client, title, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            Logger.LogInformation("Kids in Mind: '{Title}' not found", title);
            return null;
        }

        var html = await GetPageAsync(client, url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var ratings = ParseFromTitle(html) ?? ParseFromBody(html);
        if (ratings is null)
        {
            Logger.LogInformation("Kids in Mind: could not parse ratings for '{Title}'", title);
            return null;
        }

        return ratings;
    }

    private static (string FirstLetter, string Slug) KimSlugify(string title)
    {
        var clean = ScraperHelpers.StripLeadingArticle(title).ToLowerInvariant();
        foreach (var ch in new[] { "'", "’", ":", ",", ".", "!", "?", "(", ")", "&", "/", "-" })
        {
            clean = clean.Replace(ch, string.Empty, StringComparison.Ordinal);
        }

        var slug = clean.Replace(" ", string.Empty, StringComparison.Ordinal);
        var firstLetter = slug.Length > 0 ? slug[..1] : "a";
        return (firstLetter, slug);
    }

    private static KidsInMindData? ParseFromTitle(string html)
    {
        var titleMatch = _titleTagRegex.Match(html);
        if (!titleMatch.Success)
        {
            return null;
        }

        var m = _tripletRegex.Match(titleMatch.Groups[1].Value);
        if (!m.Success)
        {
            return null;
        }

        var s = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var v = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var l = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (s > 10 || v > 10 || l > 10)
        {
            return null;
        }

        return new KidsInMindData(Sex: s, Violence: v, Language: l);
    }

    private static KidsInMindData? ParseFromBody(string html)
    {
        var text = ScraperHelpers.StripTags(html);
        var sexMatch = _sexBodyRegex.Match(text);
        var violenceMatch = _violenceBodyRegex.Match(text);
        var languageMatch = _languageBodyRegex.Match(text);
        if (!sexMatch.Success || !violenceMatch.Success || !languageMatch.Success)
        {
            return null;
        }

        return new KidsInMindData(
            Sex: int.Parse(sexMatch.Groups[1].Value, CultureInfo.InvariantCulture),
            Violence: int.Parse(violenceMatch.Groups[1].Value, CultureInfo.InvariantCulture),
            Language: int.Parse(languageMatch.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private async Task<string?> FindReviewUrlAsync(HttpClient client, string title, CancellationToken cancellationToken)
    {
        var (firstLetter, slug) = KimSlugify(title);

        // Try direct URL construction first.
        var directUrl = $"{Base}/{firstLetter}/{slug}.htm";
        var direct = await GetPageAsync(client, directUrl, cancellationToken).ConfigureAwait(false);
        if (direct is not null)
        {
            return directUrl;
        }

        // Fall back to the alphabetical listing page.
        var listing = await GetPageAsync(client, $"{Base}/{firstLetter}.htm", cancellationToken).ConfigureAwait(false);
        if (listing is null)
        {
            return null;
        }

        var normTitle = ScraperHelpers.NormalizeTitle(title);

        // Also build "Last, Article" form (e.g. "Dark Knight, The").
        var normReversed = normTitle;
        var trimmed = title.Trim();
        var withoutArticle = ScraperHelpers.StripLeadingArticle(title);
        if (!string.Equals(withoutArticle, trimmed, StringComparison.Ordinal))
        {
            var article = trimmed[..(trimmed.Length - withoutArticle.Length)].Trim();
            normReversed = ScraperHelpers.NormalizeTitle(withoutArticle + ", " + article);
        }

        foreach (Match link in _anchorRegex.Matches(listing))
        {
            var normLink = ScraperHelpers.NormalizeTitle(ScraperHelpers.StripTags(link.Groups[2].Value));
            if (!normLink.Contains(normTitle, StringComparison.Ordinal) && !normLink.Contains(normReversed, StringComparison.Ordinal))
            {
                continue;
            }

            var href = link.Groups[1].Value;
            if (href.StartsWith('/'))
            {
                return Base + href;
            }

            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return $"{Base}/{firstLetter}/{href}";
            }

            return href;
        }

        return null;
    }
}