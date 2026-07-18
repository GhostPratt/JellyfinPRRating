using MediaBrowser.Model.Plugins;

namespace JellyfinPRRating.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the tag prefix used for ratings (e.g. "PR-").
    /// </summary>
    public string TagPrefix { get; set; } = "PR-";

    /// <summary>
    /// Gets or sets the name of the tag applied to vault-tier items.
    /// </summary>
    public string VaultTag { get; set; } = "Vault";

    /// <summary>
    /// Gets or sets the score at or above which an item is vault-tier.
    /// </summary>
    public int VaultThreshold { get; set; } = 19;

    /// <summary>
    /// Gets or sets the maximum numeric rating a tag may carry. Scores above
    /// this are capped (vault-tier items get the cap plus the vault tag).
    /// </summary>
    public int MaxTagRating { get; set; } = 18;
}