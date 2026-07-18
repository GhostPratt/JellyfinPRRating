using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Calculates the PR rating for a library item.
/// </summary>
public interface IPrRatingCalculator
{
    /// <summary>
    /// Calculates the raw (un-rounded) PR score for the given item.
    /// </summary>
    /// <param name="item">The item to score.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The raw score, or <c>null</c> when no data could be found.</returns>
    Task<double?> CalculateAsync(BaseItem item, CancellationToken cancellationToken);
}