using System;
using System.Collections.Generic;
using System.Linq;
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
/// Weekly task that finds items without a PR rating tag and tries to rate them.
/// </summary>
public class BackfillRatingsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PrTagService _tagService;
    private readonly ILogger<BackfillRatingsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackfillRatingsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tagService">The tag service.</param>
    /// <param name="logger">The logger.</param>
    public BackfillRatingsTask(ILibraryManager libraryManager, PrTagService tagService, ILogger<BackfillRatingsTask> logger)
    {
        _libraryManager = libraryManager;
        _tagService = tagService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Backfill PR Ratings";

    /// <inheritdoc />
    public string Key => "PRRatingBackfill";

    /// <inheritdoc />
    public string Description => "Finds library items without a PR rating tag and attempts to calculate one.";

    /// <inheritdoc />
    public string Category => "PR Rating";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var candidates = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true
        }).Where(i => PrTagService.GetExistingPrTag(i) is null).ToList();

        _logger.LogInformation("PR backfill: {Count} unrated items to process", candidates.Count);

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _tagService.RateAndTagAsync(candidates[i], cancellationToken).ConfigureAwait(false);
            progress.Report(100.0 * (i + 1) / candidates.Count);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.WeeklyTrigger,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        ];
    }
}