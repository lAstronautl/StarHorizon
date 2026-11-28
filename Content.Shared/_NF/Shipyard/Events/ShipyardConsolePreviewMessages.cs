// Horizon: shuttle layout preview in the shipyard console
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///     Ask the server for the tile layout of a vessel.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsolePreviewRequestMessage : BoundUserInterfaceMessage
{
    public readonly ProtoId<VesselPrototype> Vessel;

    public ShipyardConsolePreviewRequestMessage(ProtoId<VesselPrototype> vessel)
    {
        Vessel = vessel;
    }
}

/// <summary>
///     Tile layout of a vessel: grid indices and their colours.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsolePreviewMessage : BoundUserInterfaceMessage
{
    public readonly string Vessel;
    public readonly Vector2i[] Tiles;
    public readonly Color[] Colors;

    public ShipyardConsolePreviewMessage(string vessel, Vector2i[] tiles, Color[] colors)
    {
        Vessel = vessel;
        Tiles = tiles;
        Colors = colors;
    }
}
