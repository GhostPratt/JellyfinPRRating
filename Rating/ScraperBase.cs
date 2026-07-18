using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Base class for the source scrapers, providing shared HTTP plumbing.
/// </summary>
public abstract class ScraperBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScraperBase"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    protected ScraperBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Creates an HTTP client with the scraper timeout applied.
    /// </summary>
    /// <returns>The client.</returns>
    protected HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    /// <summary>
    /// Fetches a page with browser-like headers, returning <c>null</c> on any failure.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page HTML, or <c>null</c>.</returns>
    protected async Task<string?> GetPageAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ScraperHelpers.AddBrowserHeaders(request);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logger.LogDebug(ex, "Scraper request failed for {Url}", url);
            return null;
        }
    }
}