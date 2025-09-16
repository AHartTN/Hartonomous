using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for agent marketplace client for discovery and installation
/// </summary>
public interface IAgentMarketplace
{
    /// <summary>
    /// Searches the marketplace for agents
    /// </summary>
    /// <param name="searchTerm">Search term (name, description, tags)</param>
    /// <param name="category">Optional category filter</param>
    /// <param name="type">Optional agent type filter</param>
    /// <param name="tags">Optional tags to match</param>
    /// <param name="author">Optional author filter</param>
    /// <param name="minRating">Minimum rating filter</param>
    /// <param name="verified">Optional verified publishers only</param>
    /// <param name="sortBy">Sort criteria</param>
    /// <param name="limit">Maximum results to return</param>
    /// <param name="offset">Results offset for pagination</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Marketplace agent search results</returns>
    Task<MarketplaceSearchResult> SearchAgentsAsync(
        string? searchTerm = null,
        string? category = null,
        AgentType? type = null,
        IEnumerable<string>? tags = null,
        string? author = null,
        double? minRating = null,
        bool? verified = null,
        MarketplaceSortBy sortBy = MarketplaceSortBy.Relevance,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a marketplace agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Optional specific version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Marketplace agent entry or null if not found</returns>
    Task<MarketplaceAgentEntry?> GetAgentAsync(
        string agentId,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available versions of an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available versions</returns>
    Task<IEnumerable<AgentVersion>> GetAgentVersionsAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an agent from the marketplace
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Optional specific version (latest if not specified)</param>
    /// <param name="downloadPath">Path to download the agent to</param>
    /// <param name="verifySignature">Whether to verify digital signature</param>
    /// <param name="progress">Progress callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Download result with local path</returns>
    Task<AgentDownloadResult> DownloadAgentAsync(
        string agentId,
        string? version = null,
        string? downloadPath = null,
        bool verifySignature = true,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs an agent from the marketplace
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Optional specific version (latest if not specified)</param>
    /// <param name="configuration">Optional installation configuration</param>
    /// <param name="progress">Progress callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Installation result</returns>
    Task<AgentInstallationResult> InstallAgentAsync(
        string agentId,
        string? version = null,
        Dictionary<string, object>? configuration = null,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an installed agent to the latest version
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Optional specific version to update to</param>
    /// <param name="preserveConfiguration">Whether to preserve existing configuration</param>
    /// <param name="progress">Progress callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Update result</returns>
    Task<AgentUpdateResult> UpdateAgentAsync(
        string agentId,
        string? version = null,
        bool preserveConfiguration = true,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="removeData">Whether to remove agent data and configuration</param>
    /// <param name="force">Whether to force uninstall even if instances are running</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Uninstallation result</returns>
    Task<AgentUninstallationResult> UninstallAgentAsync(
        string agentId,
        bool removeData = false,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets list of installed agents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of installed agent information</returns>
    Task<IEnumerable<InstalledAgentInfo>> GetInstalledAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for updates to installed agents
    /// </summary>
    /// <param name="agentId">Optional specific agent to check (all if not specified)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Available updates</returns>
    Task<IEnumerable<AgentUpdateInfo>> CheckForUpdatesAsync(
        string? agentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets marketplace categories
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of categories</returns>
    Task<IEnumerable<MarketplaceCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets popular tags
    /// </summary>
    /// <param name="limit">Maximum tags to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of popular tags with usage counts</returns>
    Task<IEnumerable<TagInfo>> GetPopularTagsAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets featured agents
    /// </summary>
    /// <param name="limit">Maximum agents to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of featured marketplace agent entries</returns>
    Task<IEnumerable<MarketplaceAgentEntry>> GetFeaturedAgentsAsync(int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recommended agents based on user profile and usage
    /// </summary>
    /// <param name="userId">User ID for personalization</param>
    /// <param name="context">Optional recommendation context</param>
    /// <param name="limit">Maximum recommendations to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of recommended marketplace agent entries</returns>
    Task<IEnumerable<MarketplaceAgentEntry>> GetRecommendedAgentsAsync(
        string userId,
        Dictionary<string, object>? context = null,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a rating and review for an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="rating">Rating (1-5 stars)</param>
    /// <param name="review">Optional review text</param>
    /// <param name="version">Agent version being reviewed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Submitted review information</returns>
    Task<AgentReview> SubmitReviewAsync(
        string agentId,
        int rating,
        string? review = null,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets reviews for an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Optional version filter</param>
    /// <param name="limit">Maximum reviews to return</param>
    /// <param name="offset">Results offset for pagination</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent reviews</returns>
    Task<IEnumerable<AgentReview>> GetReviewsAsync(
        string agentId,
        string? version = null,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports an issue with a marketplace agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="issueType">Type of issue</param>
    /// <param name="description">Issue description</param>
    /// <param name="version">Agent version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Issue report ID</returns>
    Task<string> ReportIssueAsync(
        string agentId,
        IssueType issueType,
        string description,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when an agent is installed
    /// </summary>
    event EventHandler<AgentInstalledEventArgs> AgentInstalled;

    /// <summary>
    /// Event fired when an agent is updated
    /// </summary>
    event EventHandler<AgentUpdatedEventArgs> AgentUpdated;

    /// <summary>
    /// Event fired when an agent is uninstalled
    /// </summary>
    event EventHandler<AgentUninstalledEventArgs> AgentUninstalled;
}

/// <summary>
/// Marketplace search sort criteria
/// </summary>
public enum MarketplaceSortBy
{
    /// <summary>
    /// Sort by search relevance
    /// </summary>
    Relevance,

    /// <summary>
    /// Sort by name alphabetically
    /// </summary>
    Name,

    /// <summary>
    /// Sort by creation date (newest first)
    /// </summary>
    DateCreated,

    /// <summary>
    /// Sort by last update date (newest first)
    /// </summary>
    DateUpdated,

    /// <summary>
    /// Sort by download count (highest first)
    /// </summary>
    Downloads,

    /// <summary>
    /// Sort by rating (highest first)
    /// </summary>
    Rating,

    /// <summary>
    /// Sort by price (lowest first)
    /// </summary>
    Price
}

/// <summary>
/// Marketplace search result
/// </summary>
public sealed record MarketplaceSearchResult
{
    /// <summary>
    /// Search results
    /// </summary>
    public IReadOnlyList<MarketplaceAgentEntry> Agents { get; init; } = Array.Empty<MarketplaceAgentEntry>();

    /// <summary>
    /// Total number of matches
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Search facets/filters
    /// </summary>
    public Dictionary<string, IReadOnlyList<FacetValue>> Facets { get; init; } = new();

    /// <summary>
    /// Search suggestions for refinement
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Search execution time in milliseconds
    /// </summary>
    public long SearchTimeMs { get; init; }
}

/// <summary>
/// Marketplace agent entry
/// </summary>
public sealed record MarketplaceAgentEntry
{
    /// <summary>
    /// Agent definition
    /// </summary>
    public required AgentDefinition Agent { get; init; }

    /// <summary>
    /// Marketplace-specific information
    /// </summary>
    public required MarketplaceInfo Marketplace { get; init; }

    /// <summary>
    /// Publisher information
    /// </summary>
    public required PublisherInfo Publisher { get; init; }

    /// <summary>
    /// Pricing information
    /// </summary>
    public PricingInfo? Pricing { get; init; }

    /// <summary>
    /// Statistics
    /// </summary>
    public required AgentStatistics Statistics { get; init; }

    /// <summary>
    /// Screenshots and media
    /// </summary>
    public IReadOnlyList<MediaItem> Media { get; init; } = Array.Empty<MediaItem>();

    /// <summary>
    /// Related agents
    /// </summary>
    public IReadOnlyList<string> RelatedAgents { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Marketplace-specific information
/// </summary>
public sealed record MarketplaceInfo
{
    /// <summary>
    /// Marketplace entry ID
    /// </summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Entry status
    /// </summary>
    public MarketplaceEntryStatus Status { get; init; } = MarketplaceEntryStatus.Active;

    /// <summary>
    /// Verification status
    /// </summary>
    public VerificationStatus VerificationStatus { get; init; } = VerificationStatus.Unverified;

    /// <summary>
    /// Featured status
    /// </summary>
    public bool IsFeatured { get; init; }

    /// <summary>
    /// Moderation status
    /// </summary>
    public ModerationStatus ModerationStatus { get; init; } = ModerationStatus.Approved;

    /// <summary>
    /// Marketplace categories
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Age rating
    /// </summary>
    public AgeRating AgeRating { get; init; } = AgeRating.General;

    /// <summary>
    /// Content warnings
    /// </summary>
    public IReadOnlyList<string> ContentWarnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Release notes
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// Installation instructions
    /// </summary>
    public string? InstallationInstructions { get; init; }

    /// <summary>
    /// Marketplace entry creation date
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last update date
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Publisher information
/// </summary>
public sealed record PublisherInfo
{
    /// <summary>
    /// Publisher ID
    /// </summary>
    public required string PublisherId { get; init; }

    /// <summary>
    /// Publisher name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Publisher display name
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Publisher description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Publisher website
    /// </summary>
    public string? Website { get; init; }

    /// <summary>
    /// Contact email
    /// </summary>
    public string? ContactEmail { get; init; }

    /// <summary>
    /// Whether publisher is verified
    /// </summary>
    public bool IsVerified { get; init; }

    /// <summary>
    /// Publisher rating
    /// </summary>
    public double Rating { get; init; }

    /// <summary>
    /// Number of published agents
    /// </summary>
    public int AgentCount { get; init; }

    /// <summary>
    /// Total downloads across all agents
    /// </summary>
    public long TotalDownloads { get; init; }
}

/// <summary>
/// Pricing information
/// </summary>
public sealed record PricingInfo
{
    /// <summary>
    /// Pricing model
    /// </summary>
    public PricingModel Model { get; init; } = PricingModel.Free;

    /// <summary>
    /// Base price
    /// </summary>
    public decimal BasePrice { get; init; }

    /// <summary>
    /// Currency code
    /// </summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Billing period (for subscription models)
    /// </summary>
    public BillingPeriod? BillingPeriod { get; init; }

    /// <summary>
    /// Free tier limits
    /// </summary>
    public Dictionary<string, object> FreeTierLimits { get; init; } = new();

    /// <summary>
    /// Pricing tiers
    /// </summary>
    public IReadOnlyList<PricingTier> Tiers { get; init; } = Array.Empty<PricingTier>();
}

/// <summary>
/// Agent statistics
/// </summary>
public sealed record AgentStatistics
{
    /// <summary>
    /// Download count
    /// </summary>
    public long Downloads { get; init; }

    /// <summary>
    /// Number of active installations
    /// </summary>
    public long ActiveInstallations { get; init; }

    /// <summary>
    /// Average rating
    /// </summary>
    public double AverageRating { get; init; }

    /// <summary>
    /// Number of ratings
    /// </summary>
    public int RatingCount { get; init; }

    /// <summary>
    /// Number of reviews
    /// </summary>
    public int ReviewCount { get; init; }

    /// <summary>
    /// View count
    /// </summary>
    public long ViewCount { get; init; }

    /// <summary>
    /// Number of favorites/bookmarks
    /// </summary>
    public long FavoriteCount { get; init; }

    /// <summary>
    /// Rating breakdown by stars
    /// </summary>
    public Dictionary<int, int> RatingBreakdown { get; init; } = new();
}

/// <summary>
/// Media item (screenshot, video, etc.)
/// </summary>
public sealed record MediaItem
{
    /// <summary>
    /// Media type
    /// </summary>
    public MediaType Type { get; init; }

    /// <summary>
    /// Media URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Thumbnail URL
    /// </summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Caption or description
    /// </summary>
    public string? Caption { get; init; }

    /// <summary>
    /// Media order for display
    /// </summary>
    public int Order { get; init; }
}

/// <summary>
/// Agent version information
/// </summary>
public sealed record AgentVersion
{
    /// <summary>
    /// Version number
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Release date
    /// </summary>
    public DateTimeOffset ReleasedAt { get; init; }

    /// <summary>
    /// Release notes
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// Download URL
    /// </summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// File checksum
    /// </summary>
    public string? Checksum { get; init; }

    /// <summary>
    /// Whether this is a pre-release
    /// </summary>
    public bool IsPrerelease { get; init; }

    /// <summary>
    /// Minimum platform version required
    /// </summary>
    public string? MinPlatformVersion { get; init; }
}

/// <summary>
/// Facet value for search filtering
/// </summary>
public sealed record FacetValue
{
    /// <summary>
    /// Facet value
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Number of matches for this value
    /// </summary>
    public long Count { get; init; }

    /// <summary>
    /// Whether this facet is currently selected
    /// </summary>
    public bool Selected { get; init; }
}

/// <summary>
/// Download progress information
/// </summary>
public sealed record DownloadProgress
{
    /// <summary>
    /// Bytes downloaded
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// Total bytes to download
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Progress percentage
    /// </summary>
    public double ProgressPercent { get; init; }

    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public long SpeedBytesPerSecond { get; init; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Current status message
    /// </summary>
    public string? StatusMessage { get; init; }
}

/// <summary>
/// Installation progress information
/// </summary>
public sealed record InstallationProgress
{
    /// <summary>
    /// Current installation step
    /// </summary>
    public InstallationStep CurrentStep { get; init; }

    /// <summary>
    /// Progress percentage for current step
    /// </summary>
    public double StepProgress { get; init; }

    /// <summary>
    /// Overall progress percentage
    /// </summary>
    public double OverallProgress { get; init; }

    /// <summary>
    /// Current status message
    /// </summary>
    public string? StatusMessage { get; init; }
}

/// <summary>
/// Agent download result
/// </summary>
public sealed record AgentDownloadResult
{
    /// <summary>
    /// Whether download was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Local file path
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    /// Downloaded agent information
    /// </summary>
    public AgentDefinition? Agent { get; init; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Downloaded file size in bytes
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Download duration
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Agent installation result
/// </summary>
public sealed record AgentInstallationResult
{
    /// <summary>
    /// Whether installation was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Installed agent definition
    /// </summary>
    public AgentDefinition? Agent { get; init; }

    /// <summary>
    /// Installation path
    /// </summary>
    public string? InstallPath { get; init; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Installation warnings
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Installation duration
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Created agent instance (if auto-start is enabled)
    /// </summary>
    public AgentInstance? Instance { get; init; }
}

// Additional enums and types for the marketplace interface
public enum MarketplaceEntryStatus { Active, Inactive, Suspended, Deprecated }
public enum VerificationStatus { Unverified, Verified, Official }
public enum ModerationStatus { Pending, Approved, Rejected, Flagged }
public enum AgeRating { General, Teen, Mature, Adult }
public enum PricingModel { Free, OneTime, Subscription, PayPerUse }
public enum BillingPeriod { Monthly, Yearly }
public enum MediaType { Image, Video, Document }
public enum InstallationStep { Downloading, Extracting, Installing, Configuring, Starting, Complete }
public enum IssueType { Bug, Security, Performance, Compatibility, Inappropriate, Copyright, Other }

/// <summary>
/// Pricing tier information
/// </summary>
public sealed record PricingTier
{
    public required string Name { get; init; }
    public decimal Price { get; init; }
    public Dictionary<string, object> Limits { get; init; } = new();
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Installed agent information
/// </summary>
public sealed record InstalledAgentInfo
{
    public required AgentDefinition Agent { get; init; }
    public required string InstallPath { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public string? SourceMarketplace { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
}

/// <summary>
/// Agent update information
/// </summary>
public sealed record AgentUpdateInfo
{
    public required string AgentId { get; init; }
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public string? UpdateDescription { get; init; }
    public UpdateImportance Importance { get; init; } = UpdateImportance.Normal;
    public IReadOnlyList<string> BreakingChanges { get; init; } = Array.Empty<string>();
}

public enum UpdateImportance { Low, Normal, High, Critical }

/// <summary>
/// Agent update result
/// </summary>
public sealed record AgentUpdateResult
{
    public bool Success { get; init; }
    public AgentDefinition? UpdatedAgent { get; init; }
    public string? PreviousVersion { get; init; }
    public string? NewVersion { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public TimeSpan Duration { get; init; }
    public bool RequiresRestart { get; init; }
}

/// <summary>
/// Agent uninstallation result
/// </summary>
public sealed record AgentUninstallationResult
{
    public bool Success { get; init; }
    public string? RemovedPath { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> RemainingFiles { get; init; } = Array.Empty<string>();
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Marketplace category information
/// </summary>
public sealed record MarketplaceCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? IconUrl { get; init; }
    public int AgentCount { get; init; }
    public IReadOnlyList<MarketplaceCategory> Subcategories { get; init; } = Array.Empty<MarketplaceCategory>();
}

/// <summary>
/// Tag information with usage count
/// </summary>
public sealed record TagInfo
{
    public required string Tag { get; init; }
    public long UsageCount { get; init; }
    public double TrendingScore { get; init; }
}

/// <summary>
/// Agent review information
/// </summary>
public sealed record AgentReview
{
    public required string ReviewId { get; init; }
    public required string AgentId { get; init; }
    public required string UserId { get; init; }
    public string? UserDisplayName { get; init; }
    public int Rating { get; init; }
    public string? ReviewText { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int HelpfulCount { get; init; }
    public bool IsVerifiedPurchase { get; init; }
}

/// <summary>
/// Event arguments for agent installation events
/// </summary>
public class AgentInstalledEventArgs : EventArgs
{
    public required AgentInstallationResult Result { get; init; }
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
}

public class AgentUpdatedEventArgs : EventArgs
{
    public required AgentUpdateResult Result { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public class AgentUninstalledEventArgs : EventArgs
{
    public required string AgentId { get; init; }
    public required AgentUninstallationResult Result { get; init; }
    public DateTimeOffset UninstalledAt { get; init; } = DateTimeOffset.UtcNow;
}