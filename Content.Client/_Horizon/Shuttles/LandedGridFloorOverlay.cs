using System.Numerics;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Horizon.Shuttles;

/// <summary>
/// The engine draws the tiles of overlapping grids in an arbitrary order, so the floor of a shuttle sitting on a
/// landing pad can end up under the floor of the pad. This redraws the floor tiles of landable shuttles in the entity
/// layer, above the pad floor and (together with <see cref="LandedGridOcclusionSystem"/>) above pad sprites under it.
/// </summary>
public sealed class LandedGridFloorOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly IResourceCache _resources;
    private readonly ITileDefinitionManager _tileDefs;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly EntityLookupSystem _lookup;

    private readonly Dictionary<ResPath, Texture> _textureCache = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public LandedGridFloorOverlay(
        IEntityManager entManager,
        IResourceCache resources,
        ITileDefinitionManager tileDefs,
        SharedMapSystem map,
        SharedTransformSystem transform,
        EntityLookupSystem lookup)
    {
        _entManager = entManager;
        _resources = resources;
        _tileDefs = tileDefs;
        _map = map;
        _transform = transform;
        _lookup = lookup;
        ZIndex = (int) DrawDepth.FloorTiles;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entManager.EntityQueryEnumerator<LandableShuttleComponent, MapGridComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var (_, _, worldMatrix, invWorld) = _transform.GetWorldPositionRotationMatrixWithInv(xform);
            var localBounds = invWorld.TransformBox(args.WorldBounds).Enlarged(grid.TileSize);
            handle.SetTransform(worldMatrix);

            var tiles = _map.GetLocalTilesEnumerator(uid, grid, localBounds);
            while (tiles.MoveNext(out var tileRef))
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                if (_tileDefs[tileRef.Tile.TypeId] is not ContentTileDefinition def || def.Sprite is not { } spritePath)
                    continue;

                if (!TryGetTexture(spritePath, out var texture))
                    continue;

                handle.DrawTextureRect(texture, _lookup.GetLocalBounds(tileRef, grid.TileSize));
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private bool TryGetTexture(ResPath path, out Texture texture)
    {
        if (_textureCache.TryGetValue(path, out texture!))
            return true;

        if (!_resources.TryGetResource<TextureResource>(path, out var resource))
            return false;

        texture = resource.Texture;
        _textureCache[path] = texture;
        return true;
    }
}
