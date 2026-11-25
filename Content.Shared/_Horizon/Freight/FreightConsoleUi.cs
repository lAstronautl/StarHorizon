using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Freight;

[Serializable, NetSerializable]
public enum FreightConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class FreightConsoleBuiState : BoundUserInterfaceState
{
    /// <summary>Address of this console's grid (null if undefined).</summary>
    public readonly ProtoId<FreightDestinationPrototype>? Here;

    public readonly List<FreightOffer> Offers;

    /// <summary>Server CurTime at which the offers are regenerated.</summary>
    public readonly TimeSpan NextRefresh;

    /// <summary>Manifest currently in the slot (null if none).</summary>
    public readonly ProtoId<FreightDestinationPrototype>? ManifestSource;
    public readonly bool HasManifest;
    public readonly List<FreightResolvedEntry> ManifestEntries;

    public readonly int SellableCrates;
    public readonly int WrongDestinationCrates;
    public readonly int SellValue;

    public FreightConsoleBuiState(ProtoId<FreightDestinationPrototype>? here, List<FreightOffer> offers,
        TimeSpan nextRefresh, bool hasManifest, ProtoId<FreightDestinationPrototype>? manifestSource,
        List<FreightResolvedEntry> manifestEntries, int sellableCrates, int wrongDestinationCrates, int sellValue)
    {
        Here = here;
        Offers = offers;
        NextRefresh = nextRefresh;
        HasManifest = hasManifest;
        ManifestSource = manifestSource;
        ManifestEntries = manifestEntries;
        SellableCrates = sellableCrates;
        WrongDestinationCrates = wrongDestinationCrates;
        SellValue = sellValue;
    }
}

/// <summary>Take an offered order: prints its manifest.</summary>
[Serializable, NetSerializable]
public sealed class FreightTakeOfferMessage : BoundUserInterfaceMessage
{
    public int Index;

    public FreightTakeOfferMessage()
    {
    }

    public FreightTakeOfferMessage(int index)
    {
        Index = index;
    }
}

/// <summary>Release the crates of the manifest in the slot (must be at its loading point).</summary>
[Serializable, NetSerializable]
public sealed class FreightSpawnMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class FreightSellMessage : BoundUserInterfaceMessage;
