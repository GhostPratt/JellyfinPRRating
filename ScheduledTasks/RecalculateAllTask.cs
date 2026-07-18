using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using JellyfinPRRating.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.ScheduledTasks;

/// <summary>
/// Manual task that re-calculates the PR rating for every movie and series,
/// updating or removing tags where the result changed. Has no default schedule;
/// run it from the dashboard when ratings need a full refresh.
/// </summary>
public class RecalculateAllTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PrTagService _tagService;
    private readonly ILogger<RecalculateAllTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecalculateAllTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tagService">The tag service.</param>
    /// <param name="logger">The logger.</param>
    public RecalculateAllTask(ILibraryManager libraryManager, PrTagService tagService, ILogger<RecalculateAllTask> logger)
    {
        _libraryManager = libraryManager;
        _tagService = tagService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Recalculate All PR Ratings";

    /// <inheritdoc />
    public string Key => "PRRatingRecalculateAll";

    /// <inheritdoc />
    public string Description => "Re-calculates the PR rating for every movie and series. Run manually; there is no default schedule.";

    /// <inheritdoc />
    public string Category => "PR Rating";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true
        });

        _logger.LogInformation("PR full recalculation: {Count} items to process", items.Count);

        var changed = 0;
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _tagService.RateAndTagAsync(items[i], cancellationToken).ConfigureAwait(false))
            {
                changed++;
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        _logger.LogInformation("PR full recalculation finished: {Changed} of {Count} items updated", changed, items.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }
}