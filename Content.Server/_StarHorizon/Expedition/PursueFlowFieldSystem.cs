using System.Numerics;
using Content.Server.Gatherable.Components;
using Content.Shared.Doors.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Поле расстояний до игроков по проходимым тайлам карты-грида (поиск в ширину от всех живых игроков). Мобы волны
/// идут по нему «под гору» к ближайшему игроку: через двери и коридоры, а не по прямой в стену или в пустоту.
/// A* у ИИ ограничен 512 узлами, поэтому сам моб получает только короткую ножку пути вдоль поля.
/// </summary>
public sealed class PursueFlowFieldSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MapSystem _map = default!;

    /// <summary>Сколько тайлов максимум обходит поиск (защита от огромных карт).</summary>
    public int MaxNodes = 40000;

    /// <summary>Как часто перестраивать поле карты.</summary>
    public TimeSpan RebuildInterval = TimeSpan.FromSeconds(3);

    private sealed class Field
    {
        public TimeSpan Built;
        public readonly Dictionary<Vector2i, int> Distance = new();
    }

    private readonly Dictionary<EntityUid, Field> _fields = new();

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>
    /// Можно ли пройти по тайлу: тайл есть (не космос), нет породы и твёрдых объектов. Двери считаются проходимыми:
    /// мобы их открывают или выламывают.
    /// </summary>
    public bool IsWalkable(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (_map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty)
            return false;

        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<DoorComponent>(anchored))
                continue;

            if (HasComp<GatherableComponent>(anchored))
                return false;

            if (TryComp<PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }

    /// <summary>Перестраивает поле карты от позиций игроков, если оно устарело.</summary>
    public void EnsureField(EntityUid gridUid, IReadOnlyList<Vector2> playerPositions)
    {
        var now = _timing.CurTime;
        if (_fields.TryGetValue(gridUid, out var existing) && now - existing.Built < RebuildInterval)
            return;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var field = existing ?? new Field();
        field.Built = now;
        field.Distance.Clear();
        _fields[gridUid] = field;

        var queue = new Queue<Vector2i>();
        foreach (var pos in playerPositions)
        {
            var tile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, pos));
            if (field.Distance.TryAdd(tile, 0))
                queue.Enqueue(tile);
        }

        var walkable = new Dictionary<Vector2i, bool>();
        bool Walk(Vector2i t)
        {
            if (!walkable.TryGetValue(t, out var ok))
            {
                ok = IsWalkable(gridUid, grid, t);
                walkable[t] = ok;
            }

            return ok;
        }

        while (queue.Count > 0 && field.Distance.Count < MaxNodes)
        {
            var tile = queue.Dequeue();
            var dist = field.Distance[tile];

            foreach (var step in Neighbours)
            {
                var next = tile + step;
                if (field.Distance.ContainsKey(next) || !Walk(next))
                    continue;

                // По диагонали только если оба соседних прямых тайла проходимы: не срезаем углы стен.
                if (step.X != 0 && step.Y != 0
                    && (!Walk(new Vector2i(tile.X + step.X, tile.Y)) || !Walk(new Vector2i(tile.X, tile.Y + step.Y))))
                {
                    continue;
                }

                field.Distance[next] = dist + 1;
                queue.Enqueue(next);
            }
        }
    }

    /// <summary>
    /// Цель следующей ножки: идём от тайла моба «под гору» по полю на <paramref name="legTiles"/> шагов.
    /// False: моб не в поле (до игроков нет прохода или поле не построено).
    /// </summary>
    public bool TryGetLegTarget(EntityUid gridUid, Vector2 mobPos, int legTiles, out Vector2 target)
    {
        target = default;
        if (!_fields.TryGetValue(gridUid, out var field) || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var start = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, mobPos));

        // Моб может стоять не по центру тайла: берём ближайший тайл поля рядом.
        if (!field.Distance.ContainsKey(start))
        {
            Vector2i? near = null;
            var best = int.MaxValue;
            foreach (var step in Neighbours)
            {
                if (field.Distance.TryGetValue(start + step, out var d) && d < best)
                {
                    best = d;
                    near = start + step;
                }
            }

            if (near is not { } found)
                return false;

            start = found;
        }

        var current = start;
        for (var i = 0; i < legTiles; i++)
        {
            var dist = field.Distance[current];
            if (dist == 0)
                break;

            Vector2i? next = null;
            var nextDist = dist;
            foreach (var step in Neighbours)
            {
                if (field.Distance.TryGetValue(current + step, out var d) && d < nextDist)
                {
                    nextDist = d;
                    next = current + step;
                }
            }

            if (next is not { } n)
                break;

            current = n;
        }

        target = _map.GridTileToLocal(gridUid, grid, current).Position;
        return true;
    }

    /// <summary>Расстояние по полю (в тайлах) от позиции до ближайшего игрока, если позиция в поле.</summary>
    public int? DistanceAt(EntityUid gridUid, Vector2 pos)
    {
        if (!_fields.TryGetValue(gridUid, out var field) || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var tile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, pos));
        return field.Distance.TryGetValue(tile, out var d) ? d : null;
    }

    /// <summary>Убирает поля удалённых карт.</summary>
    public void Prune()
    {
        foreach (var key in new List<EntityUid>(_fields.Keys))
        {
            if (TerminatingOrDeleted(key))
                _fields.Remove(key);
        }
    }
}
