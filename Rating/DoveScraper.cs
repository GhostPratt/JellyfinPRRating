using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Scrapes Dove.org positive/negative ratings (ported from PlexRating's dove.py).
/// </summary>
public class DoveScraper : ScraperBase
{
    private const string Base = "https://dove.org";

    private static readonly Regex _anchorRegex = new("<a\\s[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex _ratingsSectionRegex = new("class=\"([^\"]*ratings-section[^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _dataRatingRegex = new("data-rating=\"(\\d+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex _numberRegex = new(@"(\d+)", RegexOptions.Compiled);
    private static readonly Regex _categoriesItemRegex = new(@"categories-item--(\d+)", RegexOptions.Compiled);
    private static readonly Regex _dataCriteriaRegex = new("data-criteria=\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> _negativeCategories = new(StringComparer.OrdinalIgnoreCase) { "sex", "language", "violence", "drugs", "nudity", "other" };
    private static readonly HashSet<string> _positiveCategories = new(StringComparer.OrdinalIgnoreCase) { "faith", "integrity" };

    /// <summary>
    /// Initializes a new instance of the <see cref="DoveScraper"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public DoveScraper(IHttpClientFactory httpClientFactory, ILogger<DoveScraper> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Scrapes Dove.org for the given movie.
    /// </summary>
    /// <param name="title">The movie title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scraped data, or <c>null</c> when not found.</returns>
    public async Task<DoveData?> ScrapeAsync(string title, int? year, CancellationToken cancellationToken)
    {
        using var client = CreateClient();

        var url = await FindReviewUrlAsync(client, title, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            Logger.LogInformation("Dove: '{Title}' not found", title);
            return null;
        }

        var html = await GetPageAsync(client, url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var ratings = ParseRatings(html);
        if (ratings is null)
        {
            Logger.LogInformation("Dove: could not parse ratings for '{Title}'", title);
            return null;
        }

        return ratings;
    }

    private static DoveData? ParseRatings(string html)
    {
        int? negTotal = null;
        int? posTotal = null;

        // Primary: parse totals from section-circle content inside ratings-section blocks.
        var sectionMatches = _ratingsSectionRegex.Matches(html);
        for (var i = 0; i < sectionMatches.Count; i++)
        {
            var match = sectionMatches[i];
            var classes = match.Groups[1].Value;
            var blockEnd = i + 1 < sectionMatches.Count ? sectionMatches[i + 1].Index : Math.Min(html.Length, match.Index + 4000);
            var block = html[match.Index..blockEnd];

            var circleIdx = block.IndexOf("section-circle", StringComparison.OrdinalIgnoreCase);
            if (circleIdx < 0)
            {
                continue;
            }

            // The total lives in data-rating="N" on the circle element; visible text
            // is the fallback (class names like rating-circle--negative-4 would
            // otherwise yield the wrong number).
            var numberMatch = _dataRatingRegex.Match(block, circleIdx);
            if (!numberMatch.Success)
            {
                numberMatch = _numberRegex.Match(ScraperHelpers.StripTags(block[circleIdx..]));
            }

            if (!numberMatch.Success)
            {
                continue;
            }

            var val = int.Parse(numberMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (classes.Contains("negative", StringComparison.OrdinalIgnoreCase))
            {
                negTotal = val;
            }
            else if (classes.Contains("positive", StringComparison.OrdinalIgnoreCase))
            {
                posTotal = val;
            }
        }

        // Fallback: sum individual category scores.
        if (negTotal is null || posTotal is null)
        {
            var negSum = 0;
            var posSum = 0;
            var foundAny = false;

            foreach (Match item in _categoriesItemRegex.Matches(html))
            {
                var score = int.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture);
                var searchEnd = Math.Min(html.Length, item.Index + 500);
                var criteriaMatch = _dataCriteriaRegex.Match(html[item.Index..searchEnd]);
                if (!criteriaMatch.Success)
                {
                    continue;
                }

                var criteria = criteriaMatch.Groups[1].Value;
                if (_negativeCategories.Contains(criteria))
                {
                    negSum += score;
                    foundAny = true;
                }
                else if (_positiveCategories.Contains(criteria))
                {
                    posSum += score;
                    foundAny = true;
                }
            }

            if (foundAny)
            {
                negTotal ??= negSum;
                posTotal ??= posSum;
            }
        }

        if (negTotal is null && posTotal is null)
        {
            return null;
        }

        return new DoveData(Positive: posTotal ?? 0, Negative: negTotal ?? 0);
    }

    private async Task<string?> FindReviewUrlAsync(HttpClient client, string title, CancellationToken cancellationToken)
    {
        var searchUrl = $"{Base}/?s={title.Trim().Replace(' ', '+')}";
        var html = await GetPageAsync(client, searchUrl, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var normTitle = ScraperHelpers.NormalizeTitle(title);
        string? partialMatch = null;

        foreach (Match link in _anchorRegex.Matches(html))
        {
            var href = link.Groups[1].Value;
            if (!href.Contains("/review/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var linkText = ScraperHelpers.StripTags(link.Groups[2].Value);
            foreach (var line in linkText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(ScraperHelpers.NormalizeTitle(line), normTitle, StringComparison.Ordinal))
                {
                    return href;
                }
            }

            if (partialMatch is null && ScraperHelpers.NormalizeTitle(linkText).Contains(normTitle, StringComparison.Ordinal))
            {
                partialMatch = href;
            }
        }

        return partialMatch;
    }
}