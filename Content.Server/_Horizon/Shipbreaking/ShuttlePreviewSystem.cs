using System.Linq;
using Content.Shared._Horizon.Shipbreaking.Derelict;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Horizon.Shipbreaking;

/// <summary>
/// Builds a coloured tile layout of a shuttle map file for UI previews. The map is loaded once on a temporary map and
/// the result is cached, so repeated requests cost nothing.
/// </summary>
public sealed class ShuttlePreviewSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;

    private readonly Dictionary<string, (Vector2i[] Tiles, Color[] Colors)> _previews = new();

    public bool TryGetPreview(string cacheKey,
        ResPath path,
        ProtoId<ShuttleMinimapPrototype> minimap,
        out (Vector2i[] Tiles, Color[] Colors) preview)
    {
        if (_previews.TryGetValue(cacheKey, out preview))
            return true;

        if (!TryBuildPreview(path, minimap, out preview))
            return false;

        _previews[cacheKey] = preview;
        return true;
    }

    private bool TryBuildPreview(ResPath path,
        ProtoId<ShuttleMinimapPrototype> minimapId,
        out (Vector2i[] Tiles, Color[] Colors) preview)
    {
        preview = default;

        if (!_proto.TryIndex(minimapId, out var minimap))
            return false;

        _map.CreateMap(out var mapId, runMapInit: false);
        try
        {
            if (!_mapLoader.TryLoadGrid(mapId, path, out var grid))
                return false;

            var tiles = new List<Vector2i>();
            var colors = new List<Color>();

            foreach (var tile in _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp))
            {
                var color = Color.Black;

                var tileId = _tileDefs[tile.Tile.TypeId].ID;
                foreach (var rule in minimap.Tiles)
                {
                    if (rule.Fits(tileId))
                        color = rule.Color;
                }

                var best = -1;
                foreach (var anchored in _map.GetAnchoredEntities(grid.Value.Owner, grid.Value.Comp, tile.GridIndices))
                {
                    var rule = LastRule(minimap.Entities, MetaData(anchored).EntityPrototype);
                    if (rule > best)
                        best = rule;
                }

                if (best >= 0)
                    color = minimap.Entities[best].Color;

                tiles.Add(tile.GridIndices);
                colors.Add(color);
            }

            preview = (tiles.ToArray(), colors.ToArray());
            return true;
        }
        finally
        {
            _map.DeleteMap(mapId);
        }
    }

    private int LastRule(List<ShuttleMinimapRule> rules, EntityPrototype? proto)
    {
        if (proto == null)
            return -1;

        var ids = new HashSet<string>();
        CollectIds(proto, ids);

        for (var i = rules.Count - 1; i >= 0; i--)
        {
            if (ids.Any(rules[i].Fits))
                return i;
        }

        return -1;
    }

    private void CollectIds(EntityPrototype proto, HashSet<string> ids)
    {
        if (!ids.Add(proto.ID) || proto.Parents == null)
            return;

        foreach (var parent in proto.Parents)
        {
            if (_proto.TryIndex<EntityPrototype>(parent, out var parentProto))
                CollectIds(parentProto, ids);
            else
                ids.Add(parent);
        }
    }
}
