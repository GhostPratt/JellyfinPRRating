namespace JellyfinPRRating.Rating;

/// <summary>
/// Ratings scraped from Common Sense Media.
/// </summary>
/// <param name="SiteRating">Age recommendation from the site.</param>
/// <param name="ParentRating">Age recommendation from parents.</param>
/// <param name="KidRating">Age recommendation from kids.</param>
/// <param name="PositiveMessage">Positive message score, 1-5.</param>
/// <param name="PositiveRole">Positive role models score, 1-5.</param>
/// <param name="Violence">Violence score, 1-5.</param>
/// <param name="Sex">Sex score, 1-5.</param>
/// <param name="Language">Language score, 1-5.</param>
/// <param name="Products">Consumerism score, 1-5.</param>
/// <param name="Drinking">Drinking/drugs score, 1-5.</param>
/// <param name="Education">Educational value score, 1-5.</param>
public record CommonSenseData(
    int SiteRating,
    int ParentRating,
    int KidRating,
    int PositiveMessage,
    int PositiveRole,
    int Violence,
    int Sex,
    int Language,
    int Products,
    int Drinking,
    int Education);

/// <summary>
/// Ratings scraped from Kids in Mind (each 0-10).
/// </summary>
/// <param name="Sex">Sex/nudity score.</param>
/// <param name="Violence">Violence/gore score.</param>
/// <param name="Language">Language/profanity score.</param>
public record KidsInMindData(int Sex, int Violence, int Language);

/// <summary>
/// Letter grades (A+ to F-) scraped from Parent Previews.
/// </summary>
/// <param name="Overall">Overall grade.</param>
/// <param name="Violence">Violence grade.</param>
/// <param name="Sex">Sexual content grade.</param>
/// <param name="Profanity">Profanity grade.</param>
/// <param name="Substance">Substance use grade.</param>
public record ParentPreviewsData(string Overall, string Violence, string Sex, string Profanity, string Substance);

/// <summary>
/// Positive/negative totals scraped from Dove.org.
/// </summary>
/// <param name="Positive">Positive content total.</param>
/// <param name="Negative">Negative content total.</param>
public record DoveData(int Positive, int Negative);