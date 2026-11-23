using System.Linq;
using Content.Server.Gatherable.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Расчищает породу (стены, которые добываются киркой: <see cref="GatherableComponent"/>) вокруг точки: дроп-под не должен
/// сажать игрока и мобов в камень.
/// </summary>
public sealed class RockClearSystem : EntitySystem
{
    [Dependency] private readonly MapSystem _map = default!;

    /// <summary>Удаляет породу в радиусе <paramref name="radius"/> тайлов вокруг <paramref name="coordinates"/>.</summary>
    public int ClearRocks(EntityCoordinates coordinates, float radius)
    {
        if (coordinates.EntityId is not { Valid: true } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return 0;

        var center = _map.TileIndicesFor(gridUid, grid, coordinates);
        var cleared = 0;
        var r = (int) MathF.Ceiling(radius);
        var rSq = radius * radius + 0.5f;

        for (var dx = -r; dx <= r; dx++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > rSq)
                    continue;

                var tile = new Vector2i(center.X + dx, center.Y + dy);
                foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile).ToList())
                {
                    if (!HasComp<GatherableComponent>(anchored))
                        continue;

                    QueueDel(anchored);
                    cleared++;
                }
            }
        }

        return cleared;
    }

    /// <summary>На тайле нет ни породы, ни других твёрдых объектов: сюда можно садиться (поды с мобами в камень не падают).</summary>
    public bool TileClear(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<GatherableComponent>(anchored))
                return false;

            if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }
}
