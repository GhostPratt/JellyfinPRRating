using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Shared helpers for the rating scrapers (ported from PlexRating's _http.py).
/// </summary>
public static class ScraperHelpers
{
    private static readonly string[] _articles = ["The ", "A ", "An "];

    private static readonly Regex _tagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Applies the browser-like headers the scrapers use to a request message.
    /// </summary>
    /// <param name="request">The request to decorate.</param>
    public static void AddBrowserHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Force HTTP/1.1: Jellyfin's HttpClient defaults to HTTP/2, and .NET's HTTP/2
        // fingerprint does not match a real browser's, so Cloudflare-fronted sources
        // (kids-in-mind, parentpreviews) 403 a "Chrome" User-Agent arriving over HTTP/2.
        // Over HTTP/1.1 the same request succeeds. RequestVersionOrLower keeps it from
        // upgrading regardless of the client's default version.
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    /// <summary>
    /// Converts a title to a URL slug ("The Dark Knight" → "the-dark-knight").
    /// </summary>
    /// <param name="title">The title to slugify.</param>
    /// <returns>The slug.</returns>
    public static string Slugify(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var slug = title.ToLowerInvariant();
        foreach (var ch in new[] { "'", "’", ":", ",", ".", "!", "?", "(", ")", "&", "/" })
        {
            slug = slug.Replace(ch, string.Empty, StringComparison.Ordinal);
        }

        slug = slug.Replace(" - ", "-", StringComparison.Ordinal).Replace("  ", " ", StringComparison.Ordinal).Trim();
        slug = slug.Replace(" ", "-", StringComparison.Ordinal);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug;
    }

    /// <summary>
    /// Normalizes a title for fuzzy comparison (lowercase, punctuation stripped).
    /// </summary>
    /// <param name="title">The title to normalize.</param>
    /// <returns>The normalized title.</returns>
    public static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var t = title.ToLowerInvariant().Trim();
        foreach (var ch in new[] { ":", "'", "’", ",", ".", "!", "?", "(", ")", "&", "/", "-", "–", "—" })
        {
            t = t.Replace(ch, " ", StringComparison.Ordinal);
        }

        while (t.Contains("  ", StringComparison.Ordinal))
        {
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        }

        return t.Trim();
    }

    /// <summary>
    /// Strips the leading English article ("The ", "A ", "An ") from a title, if present.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The title without a leading article.</returns>
    public static string StripLeadingArticle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var clean = title.Trim();
        foreach (var article in _articles)
        {
            if (clean.StartsWith(article, StringComparison.Ordinal))
            {
                return clean[article.Length..];
            }
        }

        return clean;
    }

    /// <summary>
    /// Removes HTML tags from a fragment and decodes basic entities, approximating
    /// BeautifulSoup's get_text().
    /// </summary>
    /// <param name="html">The HTML fragment.</param>
    /// <returns>The plain text.</returns>
    public static string StripTags(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var text = _tagRegex.Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text;
    }
}