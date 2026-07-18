using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Services;

/// <summary>
/// Listens for newly added library items and rates them.
/// </summary>
public sealed class ItemAddedListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly PrTagService _tagService;
    private readonly ILogger<ItemAddedListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemAddedListener"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tagService">The tag service.</param>
    /// <param name="logger">The logger.</param>
    public ItemAddedListener(ILibraryManager libraryManager, PrTagService tagService, ILogger<ItemAddedListener> logger)
    {
        _libraryManager = libraryManager;
        _tagService = tagService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Movie and not Series)
        {
            return;
        }

        var item = e.Item;
        _ = Task.Run(async () =>
        {
            try
            {
                await _tagService.RateAndTagAsync(item, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rate newly added item {Item}", item.Name);
            }
        });
    }
}