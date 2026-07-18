using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JellyfinPRRating.Configuration;
using JellyfinPRRating.Rating;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Services;

/// <summary>
/// Applies, updates and removes PR rating tags on library items.
/// </summary>
public class PrTagService
{
    private readonly IPrRatingCalculator _calculator;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PrTagService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrTagService"/> class.
    /// </summary>
    /// <param name="calculator">The rating calculator.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public PrTagService(IPrRatingCalculator calculator, ILibraryManager libraryManager, ILogger<PrTagService> logger)
    {
        _calculator = calculator;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the PR tag currently applied to the item, if any.
    /// </summary>
    /// <param name="item">The item to inspect.</param>
    /// <returns>The existing PR tag, or <c>null</c>.</returns>
    public static string? GetExistingPrTag(BaseItem item)
    {
        var prefix = Config.TagPrefix;
        return item.Tags.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates the PR rating for an item and applies/updates its tags.
    /// Existing PR/Vault tags are corrected or removed when the rating changed;
    /// no tags are touched when no data is available and none were applied before.
    /// </summary>
    /// <param name="item">The item to rate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when the item's tags were changed.</returns>
    public async Task<bool> RateAndTagAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Config;
        double? score;
        try
        {
            score = await _calculator.CalculateAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PR rating calculation failed for {Item}", item.Name);
            return false;
        }

        var existingPrTag = GetExistingPrTag(item);
        var hasVault = item.Tags.Contains(config.VaultTag, StringComparer.OrdinalIgnoreCase);

        if (score is null)
        {
            // No data: leave items alone that were never rated; keep an existing rating rather than dropping it.
            _logger.LogInformation("No PR rating data found for {Item}; leaving tags unchanged", item.Name);
            return false;
        }

        var rounded = (int)Math.Floor(score.Value);
        var isVault = rounded >= config.VaultThreshold;
        var capped = Math.Min(rounded, config.MaxTagRating);
        var newPrTag = config.TagPrefix + capped.ToString(CultureInfo.InvariantCulture);

        var changed = false;
        var tags = item.Tags.ToList();

        if (!string.Equals(existingPrTag, newPrTag, StringComparison.Ordinal))
        {
            if (existingPrTag is not null)
            {
                tags.RemoveAll(t => string.Equals(t, existingPrTag, StringComparison.OrdinalIgnoreCase));
            }

            tags.Add(newPrTag);
            changed = true;
        }

        if (isVault && !hasVault)
        {
            tags.Add(config.VaultTag);
            changed = true;
        }
        else if (!isVault && hasVault)
        {
            tags.RemoveAll(t => string.Equals(t, config.VaultTag, StringComparison.OrdinalIgnoreCase));
            changed = true;
        }

        if (changed)
        {
            item.Tags = [.. tags];
            await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied {Tag}{Vault} to {Item} (raw score {Score})", newPrTag, isVault ? " + " + config.VaultTag : string.Empty, item.Name, score.Value);
        }

        return changed;
    }
}