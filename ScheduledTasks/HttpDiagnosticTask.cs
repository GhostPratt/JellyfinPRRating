using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JellyfinPRRating.Rating;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinPRRating.ScheduledTasks;

/// <summary>
/// Diagnostic task (manual trigger only) that logs the egress IP and the exact
/// outgoing headers the plugin's HTTP client sends, plus the status from a known
/// Cloudflare-strict source. Used to determine whether the live 403s come from the
/// egress IP or from the request itself. Compares the plugin's unnamed
/// <c>CreateClient()</c> against Jellyfin's configured <see cref="NamedClient.Default"/>.
/// </summary>
public class HttpDiagnosticTask : IScheduledTask
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpDiagnosticTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpDiagnosticTask"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public HttpDiagnosticTask(IHttpClientFactory httpClientFactory, ILogger<HttpDiagnosticTask> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "PR Rating HTTP Diagnostic";

    /// <inheritdoc />
    public string Key => "PRRatingHttpDiag";

    /// <inheritdoc />
    public string Description => "Logs the egress IP and outgoing headers the plugin's HTTP client actually sends, to diagnose Cloudflare 403s.";

    /// <inheritdoc />
    public string Category => "PR Rating";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        using (var unnamed = _httpClientFactory.CreateClient())
        {
            await ProbeAsync("unnamed CreateClient()", unnamed, addHeaders: true, cancellationToken).ConfigureAwait(false);
        }

        using (var named = _httpClientFactory.CreateClient(NamedClient.Default))
        {
            await ProbeAsync("NamedClient.Default", named, addHeaders: true, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    private async Task ProbeAsync(string label, HttpClient client, bool addHeaders, CancellationToken ct)
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        _logger.LogInformation("PRDIAG [{Label}] === begin ===", label);

        await OneAsync(label, client, "egress-IP", "https://api.ipify.org", addHeaders, ct).ConfigureAwait(false);
        await OneAsync(label, client, "echo-headers", "https://postman-echo.com/headers", addHeaders, ct).ConfigureAwait(false);
        await OneAsync(label, client, "kids-in-mind", "https://kids-in-mind.com/g/goosebumps.htm", addHeaders, ct).ConfigureAwait(false);

        _logger.LogInformation("PRDIAG [{Label}] === end ===", label);
    }

    private async Task OneAsync(string label, HttpClient client, string what, string url, bool addHeaders, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (addHeaders)
            {
                ScraperHelpers.AddBrowserHeaders(req);
            }

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var snippet = body.Length > 600 ? body[..600] : body;
            _logger.LogInformation("PRDIAG [{Label}] {What} status={Status} body={Body}", label, what, (int)resp.StatusCode, snippet.Replace('\n', ' '));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PRDIAG [{Label}] {What} FAILED: {Type}: {Msg}", label, what, ex.GetType().Name, ex.Message);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}