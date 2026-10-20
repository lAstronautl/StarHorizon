using System.Numerics;
using Content.Shared._Horizon.Shipbreaking;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client._Horizon.Shipbreaking;

/// <summary>
/// Draws the cuts of <see cref="GridCutsComponent"/> as thin gaps along the seams between tiles.
/// </summary>
public sealed class GridCutOverlay : Overlay
{
    // Same as the background seen through a hole in the hull.
    private static readonly Color GapColor = Color.Black;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;

    public GridCutOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = entManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entManager.EntityQueryEnumerator<GridCutsComponent, MapGridComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var cuts, out var grid, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            handle.SetTransform(_transform.GetWorldMatrix(uid));

            var size = grid.TileSize;
            var half = GridCutsComponent.CutWidthPixels / 32f / 2f * size;

            foreach (var (tile, edges) in cuts.Cuts)
            {
                // Every cut is stored on both tiles, so only draw the north and east ones.
                if (edges.HasFlag(CutEdges.North))
                    handle.DrawRect(CutEdges.North.GetRect(tile, size, half), GapColor);

                if (edges.HasFlag(CutEdges.East))
                    handle.DrawRect(CutEdges.East.GetRect(tile, size, half), GapColor);
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }
}
