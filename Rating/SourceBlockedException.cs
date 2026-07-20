using System;

namespace JellyfinPRRating.Rating;

/// <summary>
/// Thrown when a rating source rejects the request with HTTP 403, indicating the
/// source is blocking us rather than that no review exists. Callers treat this as
/// "could not determine a rating" and must not score with the source silently
/// missing, otherwise a transient block would write an inflated rating.
/// </summary>
public sealed class SourceBlockedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourceBlockedException"/> class.
    /// </summary>
    public SourceBlockedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceBlockedException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public SourceBlockedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceBlockedException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SourceBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceBlockedException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="url">The URL that was blocked.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public SourceBlockedException(string message, string url, Exception? innerException = null)
        : base(message, innerException)
    {
        Url = url;
    }

    /// <summary>
    /// Gets the URL that returned the blocking response, if known.
    /// </summary>
    public string? Url { get; }
}