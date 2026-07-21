using System;
using System.Net;
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
    // A plugin-owned handler used instead of Jellyfin's IHttpClientFactory clients.
    // The Cloudflare-fronted sources (kids-in-mind, parentpreviews) 403 based on the
    // TLS ClientHello fingerprint: a request that resumes a TLS session carries a
    // pre_shared_key extension (JA4 t13d1212) and is blocked, while a full handshake
    // (JA4 t13d1211) is allowed. A long-running Jellyfin process has a warm TLS session
    // cache and resumes, so it is blocked; a freshly-started client is not. AllowTlsResume
    // = false forces a full handshake on every connection, so the pre_shared_key never
    // appears. (Also deliberately no ApplicationProtocols, to avoid offering ALPN/h2;
    // requests stay HTTP/1.1 via the request version in ScraperHelpers.)
    // Shared and long-lived to reuse connections; PooledConnectionLifetime bounds DNS staleness.
    private static readonly SocketsHttpHandler _handler = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        SslOptions = { AllowTlsResume = false },
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ScraperBase"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory (unused; retained for DI compatibility).</param>
    /// <param name="logger">The logger.</param>
    protected ScraperBase(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _ = httpClientFactory;
        Logger = logger;
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Creates an HTTP/1.1 client with the scraper timeout applied.
    /// </summary>
    /// <returns>The client.</returns>
    protected static HttpClient CreateClient()
    {
        return new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    /// <summary>
    /// Fetches a page with browser-like headers, returning <c>null</c> on any failure.
    /// Transient failures (network errors, timeouts, HTTP 5xx/429) are retried once;
    /// non-404 failures are logged as warnings so a blocked or unreachable source is
    /// distinguishable from a missing review.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page HTML, or <c>null</c>.</returns>
    protected async Task<string?> GetPageAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        var (_, html) = await GetPageWithFinalUrlAsync(client, url, cancellationToken).ConfigureAwait(false);
        return html;
    }

    /// <summary>
    /// Fetches a page like <see cref="GetPageAsync"/>, additionally returning the
    /// final URL after redirects.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final URL and page HTML, or <c>null</c>s.</returns>
    protected async Task<(string? FinalUrl, string? Html)> GetPageWithFinalUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ScraperHelpers.AddBrowserHeaders(request);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                    var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return (finalUrl, html);
                }

                var status = (int)response.StatusCode;
                if ((status >= 500 || status == 429) && attempt == 0)
                {
                    Logger.LogDebug("HTTP {Status} from {Url}; retrying", status, url);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    // A 403 means the source is blocking us, not that the review is
                    // missing. Signal that distinctly so the calculator aborts rather
                    // than scoring with this source silently absent.
                    Logger.LogWarning("HTTP 403 from {Url}; the source is blocking requests", url);
                    throw new SourceBlockedException($"HTTP 403 from {url}; the source is blocking requests", url);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Logger.LogDebug("HTTP 404 from {Url}", url);
                }
                else
                {
                    Logger.LogWarning("HTTP {Status} from {Url}; the source may be blocking requests", status, url);
                }

                return (null, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt == 0)
                {
                    Logger.LogDebug(ex, "Request failed for {Url}; retrying", url);
                    continue;
                }

                Logger.LogWarning(ex, "Request failed for {Url} after retry", url);
                return (null, null);
            }
        }

        return (null, null);
    }
}