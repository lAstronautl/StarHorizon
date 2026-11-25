using Content.Shared.CartridgeLoader;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Marketplace;

/// <summary>
/// PDA program for browsing a marketplace network. Listings can only be viewed; requests can be posted,
/// but offers are handled at a marketplace terminal.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MarketplaceCartridgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Network = "Sector";

    [DataField, AutoNetworkedField]
    public bool Anonymous;

    [DataField, AutoNetworkedField]
    public bool BlackMarket;

    /// <summary>
    /// Shown to the player; should match the terminals of this network.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TaxRate = 0.1f;

    [DataField]
    public int MaxReward = 10_000_000;

    [DataField]
    public int MaxRequestsPerRequester = 10;
}

[Serializable, NetSerializable]
public sealed class MarketplaceCartridgeState : BoundUserInterfaceState
{
    public List<MarketplaceListingData> Listings = new();
    public List<MarketplaceRequestData> Requests = new();
    public int Balance;
    public bool HasAccount;
    public float TaxRate;
    public int MaxReward;
    public int MaxRequestsPerPlayer;
}

[Serializable, NetSerializable]
public sealed class MarketplaceCartridgeCreateRequestEvent(string title, string description, int quantity, int reward)
    : CartridgeMessageEvent
{
    public readonly string Title = title;
    public readonly string Description = description;
    public readonly int Quantity = quantity;
    public readonly int Reward = reward;
}
