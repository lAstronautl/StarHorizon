using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Freight;

[Serializable, NetSerializable]
public enum FreightConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public readonly record struct FreightManifestUiEntry(string CrateName, int Count, int Price, string Destinations);

[Serializable, NetSerializable]
public sealed class FreightConsoleBuiState : BoundUserInterfaceState
{
    public readonly string Destination;
    public readonly bool HasManifest;
    public readonly string Addressee;
    public readonly List<FreightManifestUiEntry> Entries;
    public readonly int SellableCrates;
    public readonly int WrongDestinationCrates;
    public readonly int SellValue;

    public FreightConsoleBuiState(string destination, bool hasManifest, string addressee,
        List<FreightManifestUiEntry> entries, int sellableCrates, int wrongDestinationCrates, int sellValue)
    {
        Destination = destination;
        HasManifest = hasManifest;
        Addressee = addressee;
        Entries = entries;
        SellableCrates = sellableCrates;
        WrongDestinationCrates = wrongDestinationCrates;
        SellValue = sellValue;
    }
}

[Serializable, NetSerializable]
public sealed class FreightSpawnMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class FreightSellMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class FreightRefreshMessage : BoundUserInterfaceMessage;
