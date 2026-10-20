using System.Numerics;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Horizon.Shipbreaking;

/// <summary>
/// While a <see cref="ShipCutterComponent"/> is held, shows where a click would cut, the way the entity
/// placement ghost works in the admin spawn menu: green where a cut can be made, red where it can not.
/// </summary>
public sealed class ShipCutterGhostOverlay : Overlay
{
    private static readonly Color ValidColor = Color.Lime.WithAlpha(0.7f);
    private static readonly Color InvalidColor = Color.Red.WithAlpha(0.6f);

    // The ghost is drawn a bit wider than the cut itself, so it can be seen.
    private const float GhostWidthPixels = GridCutsComponent.CutWidthPixels * 2f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly IEntityManager _entManager;
    private readonly SharedHandsSystem _hands;
    private readonly SharedShipCutterSystem _cutter;
    private readonly SharedTransformSystem _transform;

    public ShipCutterGhostOverlay(IEntityManager entManager)
    {
        IoCManager.InjectDependencies(this);

        _entManager = entManager;
        _hands = entManager.System<SharedHandsSystem>();
        _cutter = entManager.System<SharedShipCutterSystem>();
        _transform = entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_hands.TryGetActiveItem(player, out var held) ||
            !_entManager.TryGetComponent(held, out ShipCutterComponent? cutter))
            return;

        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return;

        var mapPos = _eye.PixelToMap(mouse);
        if (mapPos.MapId != args.MapId ||
            !_cutter.TryGetSeam(mapPos, cutter.EdgeTolerance, out var gridUid, out var grid, out var tile, out var edge))
            return;

        var valid = _cutter.CanCut(gridUid, grid, tile, edge);
        var half = GhostWidthPixels / 32f / 2f * grid.TileSize;

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(gridUid));
        handle.DrawRect(edge.GetRect(tile, grid.TileSize, half), valid ? ValidColor : InvalidColor);
        handle.SetTransform(Matrix3x2.Identity);
    }
}
