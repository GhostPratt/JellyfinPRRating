using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Calculates the PR score for an item. Ported from the PlexRating Python project:
/// movies combine Common Sense Media (base), Kids in Mind, Parent Previews and Dove;
/// TV shows use Common Sense Media only with stronger adjustments. Common Sense
/// data is required — without it no score is produced.
/// </summary>
public class PrRatingCalculator : IPrRatingCalculator
{
    private static readonly string[] _positiveGrades = ["A", "A+", "A-"];
    private static readonly string[] _negativeGrades = ["C-", "D+", "D", "D-", "F+", "F", "F-"];

    private readonly CommonSenseScraper _commonSense;
    private readonly KidsInMindScraper _kidsInMind;
    private readonly ParentPreviewsScraper _parentPreviews;
    private readonly DoveScraper _dove;
    private readonly ILogger<PrRatingCalculator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrRatingCalculator"/> class.
    /// </summary>
    /// <param name="commonSense">The Common Sense Media scraper.</param>
    /// <param name="kidsInMind">The Kids in Mind scraper.</param>
    /// <param name="parentPreviews">The Parent Previews scraper.</param>
    /// <param name="dove">The Dove scraper.</param>
    /// <param name="logger">The logger.</param>
    public PrRatingCalculator(
        CommonSenseScraper commonSense,
        KidsInMindScraper kidsInMind,
        ParentPreviewsScraper parentPreviews,
        DoveScraper dove,
        ILogger<PrRatingCalculator> logger)
    {
        _commonSense = commonSense;
        _kidsInMind = kidsInMind;
        _parentPreviews = parentPreviews;
        _dove = dove;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<double?> CalculateAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var title = item.Name;
        var year = item.ProductionYear;

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return item switch
        {
            Series => await CalculateTvAsync(title, year, cancellationToken).ConfigureAwait(false),
            Movie => await CalculateMovieAsync(title, year, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
    }

    private static double ApplyCommonSenseBase(CommonSenseData cs)
    {
        return ((cs.SiteRating * 3) + (cs.ParentRating * 2) + cs.KidRating) / 6.0;
    }

    private async Task<double?> CalculateMovieAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var cs = await _commonSense.ScrapeAsync(title, year, isTv: false, cancellationToken).ConfigureAwait(false);
        if (cs is null)
        {
            _logger.LogInformation("No Common Sense Media data for movie '{Title}'; cannot calculate PR rating", title);
            return null;
        }

        var kim = await _kidsInMind.ScrapeAsync(title, year, cancellationToken).ConfigureAwait(false);
        var pp = await _parentPreviews.ScrapeAsync(title, year, cancellationToken).ConfigureAwait(false);
        var dove = await _dove.ScrapeAsync(title, year, cancellationToken).ConfigureAwait(false);

        var age = ApplyCommonSenseBase(cs);

        // Positive factors lower the age.
        if (cs.PositiveMessage > 3)
        {
            age -= 0.5;
        }

        if (cs.PositiveMessage > 4)
        {
            age -= 0.25;
        }

        if (cs.PositiveRole > 3)
        {
            age -= 0.5;
        }

        if (cs.PositiveRole > 4)
        {
            age -= 0.25;
        }

        if (cs.Education > 3)
        {
            age -= 0.5;
        }

        // Negative factors raise the age.
        if (cs.Drinking > 3)
        {
            age += 0.5;
        }

        if (cs.Drinking > 4)
        {
            age += 0.25;
        }

        if (cs.Products > 3)
        {
            age += 0.25;
        }

        if (cs.Language > 3)
        {
            age += 0.5;
        }

        if (cs.Language > 4)
        {
            age += 0.25;
        }

        if (cs.Sex > 3)
        {
            age += 1.0;
        }

        if (cs.Sex > 4)
        {
            age += 1.0;
        }

        if (cs.Violence > 3)
        {
            age += 1.0;
        }

        if (cs.Violence > 4)
        {
            age += 1.0;
        }

        // Kids in Mind adjustments.
        if (kim is not null)
        {
            if (kim.Sex < 3)
            {
                age -= 0.3;
            }

            if (kim.Language < 3)
            {
                age -= 0.3;
            }

            if (kim.Violence < 3)
            {
                age -= 0.3;
            }

            if (kim.Sex > 5)
            {
                age += 0.5;
            }

            if (kim.Language > 5)
            {
                age += 0.5;
            }

            if (kim.Violence > 5)
            {
                age += 0.5;
            }
        }

        // Parent Previews adjustments.
        if (pp is not null)
        {
            foreach (var category in new[] { pp.Overall, pp.Sex, pp.Substance, pp.Profanity, pp.Violence })
            {
                if (Array.IndexOf(_positiveGrades, category) >= 0)
                {
                    age -= 0.1;
                }

                if (Array.IndexOf(_negativeGrades, category) >= 0)
                {
                    age += 0.5;
                }
            }
        }

        // Dove adjustments.
        if (dove is not null)
        {
            age += (dove.Negative * 0.2) - (dove.Positive * 0.2);
        }

        var sources = 1 + (kim is not null ? 1 : 0) + (pp is not null ? 1 : 0) + (dove is not null ? 1 : 0);
        _logger.LogInformation("PR rating for movie '{Title}': {Age:F1} ({Sources}/4 sources)", title, age, sources);
        return age;
    }

    private async Task<double?> CalculateTvAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var cs = await _commonSense.ScrapeAsync(title, year, isTv: true, cancellationToken).ConfigureAwait(false);
        if (cs is null)
        {
            _logger.LogInformation("No Common Sense Media data for TV show '{Title}'; cannot calculate PR rating", title);
            return null;
        }

        var age = ApplyCommonSenseBase(cs);

        // Positive factors (stronger than movies).
        if (cs.PositiveMessage > 3)
        {
            age -= 0.75;
        }

        if (cs.PositiveMessage > 4)
        {
            age -= 0.5;
        }

        if (cs.PositiveRole > 3)
        {
            age -= 0.75;
        }

        if (cs.PositiveRole > 4)
        {
            age -= 0.5;
        }

        if (cs.Education > 3)
        {
            age -= 0.75;
        }

        // Negative factors (stronger than movies for some).
        if (cs.Drinking > 3)
        {
            age += 0.75;
        }

        if (cs.Drinking > 4)
        {
            age += 0.5;
        }

        if (cs.Products > 3)
        {
            age += 0.5;
        }

        if (cs.Language > 3)
        {
            age += 0.75;
        }

        if (cs.Language > 4)
        {
            age += 0.5;
        }

        if (cs.Sex > 3)
        {
            age += 1.0;
        }

        if (cs.Sex > 4)
        {
            age += 1.0;
        }

        if (cs.Violence > 3)
        {
            age += 1.0;
        }

        if (cs.Violence > 4)
        {
            age += 1.0;
        }

        _logger.LogInformation("PR rating for TV show '{Title}': {Age:F1}", title, age);
        return age;
    }
}