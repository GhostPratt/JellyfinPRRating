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
/// Scrapes Common Sense Media ratings (ported from PlexRating's commonsense.py).
/// </summary>
public class CommonSenseScraper
{
    private const string Base = "https://www.commonsensemedia.org";

    private static readonly Dictionary<string, Regex> _detailPatterns = new(StringComparer.Ordinal)
    {
        ["violence"] = new Regex("csm_review_rating_details_violence[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["sex"] = new Regex("csm_review_rating_details_sex[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["language"] = new Regex("csm_review_rating_details_language[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["consumerism"] = new Regex("csm_review_rating_details_consumerism[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["drugs"] = new Regex("csm_review_rating_details_drugs[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["message"] = new Regex("csm_review_rating_details_message[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["role_model"] = new Regex("csm_review_rating_details_role_model[\"\\s:]+(\\d+)", RegexOptions.Compiled),
        ["education"] = new Regex("csm_review_rating_details_education[\"\\s:]+(\\d+)", RegexOptions.Compiled),
    };

    private static readonly Regex _siteAgeRegex = new("csm_review_rating_age[\"\\s:]+(\\d+)", RegexOptions.Compiled);
    private static readonly Regex _typicalAgeRegex = new("\"typicalAgeRange\"\\s*:\\s*\"(\\d+)\\+?\"", RegexOptions.Compiled);
    private static readonly Regex _parentAgeRegex = new(@"age\s+(\d+)\+.*?parent\s+review", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex _kidAgeRegex = new(@"age\s+(\d+)\+.*?kid\s+review", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex _datePublishedRegex = new("\"datePublished\"\\s*:\\s*\"(\\d{4})", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CommonSenseScraper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommonSenseScraper"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public CommonSenseScraper(IHttpClientFactory httpClientFactory, ILogger<CommonSenseScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Scrapes Common Sense Media for the given title.
    /// </summary>
    /// <param name="title">The item title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="isTv">Whether the item is a TV show (uses tv-reviews path).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scraped data, or <c>null</c> when not found.</returns>
    public async Task<CommonSenseData?> ScrapeAsync(string title, int? year, bool isTv, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var url = await FindReviewUrlAsync(client, title, year, isTv, cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            _logger.LogInformation("Common Sense Media: '{Title}' not found", title);
            return null;
        }

        var html = await GetStringAsync(client, url, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        var details = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, regex) in _detailPatterns)
        {
            var m = regex.Match(html);
            if (m.Success)
            {
                details[key] = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        int? siteAge = null;
        var siteMatch = _siteAgeRegex.Match(html);
        if (siteMatch.Success)
        {
            siteAge = int.Parse(siteMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var typicalMatch = _typicalAgeRegex.Match(html);
            if (typicalMatch.Success)
            {
                siteAge = int.Parse(typicalMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        if (siteAge is null or 0)
        {
            _logger.LogInformation("Common Sense Media: could not parse site age for '{Title}'", title);
            return null;
        }

        var parentMatch = _parentAgeRegex.Match(html);
        var kidMatch = _kidAgeRegex.Match(html);
        var parentAge = parentMatch.Success ? int.Parse(parentMatch.Groups[1].Value, CultureInfo.InvariantCulture) : siteAge.Value;
        var kidAge = kidMatch.Success ? int.Parse(kidMatch.Groups[1].Value, CultureInfo.InvariantCulture) : siteAge.Value;

        return new CommonSenseData(
            SiteRating: siteAge.Value,
            ParentRating: parentAge,
            KidRating: kidAge,
            PositiveMessage: details.GetValueOrDefault("message"),
            PositiveRole: details.GetValueOrDefault("role_model"),
            Violence: details.GetValueOrDefault("violence"),
            Sex: details.GetValueOrDefault("sex"),
            Language: details.GetValueOrDefault("language"),
            Products: details.GetValueOrDefault("consumerism"),
            Drinking: details.GetValueOrDefault("drugs"),
            Education: details.GetValueOrDefault("education"));
    }

    private async Task<string?> FindReviewUrlAsync(HttpClient client, string title, int? year, bool isTv, CancellationToken cancellationToken)
    {
        var slug = ScraperHelpers.Slugify(title);
        var path = isTv ? "tv-reviews" : "movie-reviews";

        var slugsToTry = new List<string> { slug };
        for (var i = 0; i < 5; i++)
        {
            slugsToTry.Add(slug + "-" + i.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var s in slugsToTry)
        {
            var url = $"{Base}/{path}/{s}";
            var (finalUrl, html) = await GetWithFinalUrlAsync(client, url, cancellationToken).ConfigureAwait(false);
            if (html is null || finalUrl is null || !finalUrl.Contains($"/{path}/", StringComparison.Ordinal))
            {
                continue;
            }

            if (year is null)
            {
                return finalUrl;
            }

            var yearMatch = _datePublishedRegex.Match(html);
            if (yearMatch.Success)
            {
                var pageYear = int.Parse(yearMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(pageYear - year.Value) <= 1)
                {
                    return finalUrl;
                }
            }
        }

        // Year-checking failed: fall back to the first valid page.
        if (year is not null)
        {
            var url = $"{Base}/{path}/{slug}";
            var (finalUrl, html) = await GetWithFinalUrlAsync(client, url, cancellationToken).ConfigureAwait(false);
            if (html is not null && finalUrl is not null && finalUrl.Contains($"/{path}/", StringComparison.Ordinal))
            {
                return finalUrl;
            }
        }

        return null;
    }

    private async Task<(string? FinalUrl, string? Html)> GetWithFinalUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ScraperHelpers.AddBrowserHeaders(request);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (finalUrl, html);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Common Sense Media request failed for {Url}", url);
            return (null, null);
        }
    }

    private async Task<string?> GetStringAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        var (_, html) = await GetWithFinalUrlAsync(client, url, cancellationToken).ConfigureAwait(false);
        return html;
    }
}