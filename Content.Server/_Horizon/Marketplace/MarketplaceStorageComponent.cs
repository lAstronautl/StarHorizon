using Robust.Shared.Network;

namespace Content.Server._Horizon.Marketplace;

/// <summary>
/// Hidden entity in null-space that holds every listed item of the sector-wide marketplace for the current round.
/// </summary>
[RegisterComponent]
public sealed partial class MarketplaceStorageComponent : Component
{
    public const string ContainerId = "marketplace_storage";

    [ViewVariables]
    public Dictionary<int, MarketplaceListing> Listings = new();

    [ViewVariables]
    public int NextId = 1;

    /// <summary>
    /// Earnings of sellers who were offline when their goods sold. Paid out the next time they open a marketplace console.
    /// </summary>
    [ViewVariables]
    public Dictionary<(NetUserId UserId, string CharacterName), int> PendingPayouts = new();
}

public sealed class MarketplaceListing
{
    [ViewVariables]
    public int Id;

    /// <summary>
    /// The listed entities. A merged stack listing may hold several stacks once a single stack's limit is reached.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Items = new();

    [ViewVariables]
    public string Name = string.Empty;

    [ViewVariables]
    public string? ProtoId;

    /// <summary>
    /// Stack type if the listing is a stackable resource, otherwise null.
    /// </summary>
    [ViewVariables]
    public string? StackType;

    [ViewVariables]
    public int UnitPrice;

    [ViewVariables]
    public NetUserId SellerUserId;

    [ViewVariables]
    public string SellerCharacterName = string.Empty;

    [ViewVariables]
    public string SellerIdName = string.Empty;

    [ViewVariables]
    public string GridName = string.Empty;

    [ViewVariables]
    public TimeSpan ListedAt;
}
