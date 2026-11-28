using Content.Server._Horizon.Shipbreaking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.Shipyard;

/// <summary>
/// Answers shipyard console requests for a vessel layout preview.
/// </summary>
public sealed class ShipyardPreviewSystem : EntitySystem
{
    private const string Minimap = "Default";

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ShuttlePreviewSystem _preview = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsolePreviewRequestMessage>(OnPreviewRequest);
    }

    private void OnPreviewRequest(Entity<ShipyardConsoleComponent> ent, ref ShipyardConsolePreviewRequestMessage args)
    {
        if (!_proto.TryIndex(args.Vessel, out var vessel))
            return;

        if (!_preview.TryGetPreview($"vessel:{vessel.ID}", vessel.ShuttlePath, Minimap, out var preview))
            return;

        _ui.ServerSendUiMessage(ent.Owner, args.UiKey,
            new ShipyardConsolePreviewMessage(vessel.ID, preview.Tiles, preview.Colors), args.Actor);
    }
}
