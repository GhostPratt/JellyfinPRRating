using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Scrapes Parent Previews letter grades (ported from PlexRating's parentpreviews.py).
/// </summary>
public class ParentPreviewsScraper : ScraperBase
{
    private const string Base = "https://parentpreviews.com";
    private const string GradePattern = "[A-F][+-]?";

    private static readonly Dictionary<string, Regex[]> _categoryPatterns = new(StringComparer.Ordinal)
    {
        ["overall"] = [new Regex(@"Overall\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase)],
        ["violence"] = [new Regex(@"Violence\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase)],
        ["sex"] =
        [
            new Regex(@"Sexual\s+Content\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"Sex\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        ["profanity"] =
        [
            new Regex(@"Profanity\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"Language\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        ["substance"] =
        [
            new Regex(@"Substance\s+Use\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"Drugs?\s*(?:/\s*Alcohol)?\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"Alcohol\s*(?:/\s*Drug)?\s*(?:Use)?\s*[:\-]?\s*(" + GradePattern + ")", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentPreviewsScraper"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public ParentPreviewsScraper(IHttpClientFactory httpClientFactory, ILogger<ParentPreviewsScraper> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <summary>
    /// Scrapes Parent Previews for the given movie.
    /// </summary>
    /// <param name="title">The movie title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scraped data, or <c>null</c> when not found.</returns>
    public async Task<ParentPreviewsData?> ScrapeAsync(string title, int? year, CancellationToken cancellationToken)
    {
        using var client = CreateClient();

        var html = await FindReviewPageAsync(client, title, year, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            Logger.LogInformation("Parent Previews: '{Title}' not found", title);
            return null;
        }

        var grades = ParseGrades(html);
        if (grades is null)
        {
            Logger.LogInformation("Parent Previews: could not parse grades for '{Title}'", title);
            return null;
        }

        return new ParentPreviewsData(
            Overall: grades.GetValueOrDefault("overall", string.Empty),
            Violence: grades.GetValueOrDefault("violence", string.Empty),
            Sex: grades.GetValueOrDefault("sex", string.Empty),
            Profanity: grades.GetValueOrDefault("profanity", string.Empty),
            Substance: grades.GetValueOrDefault("substance", string.Empty));
    }

    private static Dictionary<string, string>? ParseGrades(string html)
    {
        var text = ScraperHelpers.StripTags(html);
        var grades = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, patterns) in _categoryPatterns)
        {
            foreach (var pattern in patterns)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    grades[key] = match.Groups[1].Value.ToUpperInvariant();
                    break;
                }
            }
        }

        return grades.Count > 0 ? grades : null;
    }

    private async Task<string?> FindReviewPageAsync(HttpClient client, string title, int? year, CancellationToken cancellationToken)
    {
        var slugsToTry = new List<string> { ScraperHelpers.Slugify(title) };

        var withoutArticle = ScraperHelpers.StripLeadingArticle(title);
        if (!string.Equals(withoutArticle, title.Trim(), StringComparison.Ordinal))
        {
            slugsToTry.Insert(0, ScraperHelpers.Slugify(withoutArticle));
        }

        if (year is not null)
        {
            slugsToTry.Add(ScraperHelpers.Slugify($"{title} {year}"));
        }

        foreach (var slug in slugsToTry)
        {
            var url = $"{Base}/movie-reviews/{slug}";
            var html = await GetPageAsync(client, url, cancellationToken).ConfigureAwait(false);
            if (html is null || !html.Contains("<title>", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Verify it's not a generic 404 page served with HTTP 200.
            var head = html.Length > 2000 ? html[..2000] : html;
            if (!head.Contains("page not found", StringComparison.OrdinalIgnoreCase))
            {
                return html;
            }
        }

        return null;
    }
}