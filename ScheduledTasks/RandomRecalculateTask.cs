using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
/// Daily task that picks one random item from a random library and re-calculates
/// its PR rating, updating or removing tags when the result changed.
/// </summary>
public class RandomRecalculateTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PrTagService _tagService;
    private readonly ILogger<RandomRecalculateTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RandomRecalculateTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tagService">The tag service.</param>
    /// <param name="logger">The logger.</param>
    public RandomRecalculateTask(ILibraryManager libraryManager, PrTagService tagService, ILogger<RandomRecalculateTask> logger)
    {
        _libraryManager = libraryManager;
        _tagService = tagService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Re-check Random PR Rating";

    /// <inheritdoc />
    public string Key => "PRRatingRandomRecalc";

    /// <inheritdoc />
    public string Description => "Picks a random item from a random library and re-calculates its PR rating.";

    /// <inheritdoc />
    public string Category => "PR Rating";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var libraries = _libraryManager.GetUserRootFolder().Children.ToList();
        if (libraries.Count == 0)
        {
            return;
        }

        var library = libraries[RandomNumberGenerator.GetInt32(libraries.Count)];

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
            ParentId = library.Id
        });

        if (items.Count == 0)
        {
            _logger.LogInformation("PR random re-check: library {Library} has no rateable items", library.Name);
            return;
        }

        var item = items[RandomNumberGenerator.GetInt32(items.Count)];
        _logger.LogInformation("PR random re-check: re-rating {Item} from library {Library}", item.Name, library.Name);

        await _tagService.RateAndTagAsync(item, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
            }
        ];
    }
}