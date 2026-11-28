using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Мобы с <see cref="PursuePlayerComponent"/> всегда знают, где ближайший живой игрок на их карте (зрение и аггро не
/// нужны), и идут к нему: ставим им FollowTarget, а корень ИИ меняем на вариант с погоней.
/// </summary>
public sealed class PursuePlayerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly ExpeditionTileSystem _tiles = default!;
    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _container = default!;

    /// <summary>
    /// До какой дистанции дальники идут к цели погони. Если игрок в прямой видимости, это дальность стрельбы
    /// (RangedRange); если его не видно (за стеной) или впереди промежуточная точка пути, подходят вплотную,
    /// иначе встали бы в 10 м от игрока за стеной и не стреляли.
    /// </summary>
    public const string PursueRangeKey = "PursueRange";

    /// <summary>До какой дистанции ближники идут к цели погони: ближний бой, к ножке пути до центра клетки.</summary>
    public const string PursueMeleeRangeKey = "PursueMeleeRange";
    private const float ClosingRange = 1.5f;
    private const float LegRange = 0.3f;

    [Dependency] private readonly Content.Shared.Examine.ExamineSystemShared _examine = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;

    private const string MeleeRoot = "PursueMeleeCompound";
    private const string RangedRoot = "PursueRangedCompound";
    private const string HumanoidRoot = "PursueHumanoidCompound";

    private TimeSpan _nextUpdate;
    private readonly List<(EntityUid Uid, MapId Map, Vector2 Pos)> _players = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PursuePlayerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<PursuePlayerComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<HTNComponent>(ent, out var htn))
            return;

        // Корень с погоней подбираем по исходному: дальники остаются дальниками (стреляют), а не лезут в ближний бой.
        var original = htn.RootTask.Task.ToString();
        var root = original switch
        {
            "SimpleHumanoidHostileCompound" => HumanoidRoot,
            "SimpleRangedHostileCompound" => RangedRoot,
            _ when original.Contains("Ranged") => RangedRoot,
            _ => MeleeRoot,
        };
        htn.RootTask = new HTNCompoundTask { Task = root };
        htn.Blackboard.SetValue(PursueRangeKey, ClosingRange);
        htn.Blackboard.SetValue(PursueMeleeRangeKey, htn.Blackboard.GetValueOrDefault<float>("MeleeRange", EntityManager));
        // Стены в погоне не ломаем: иначе путь прокладывается сквозь стену, и моб (ксенос) долбит её вместо обхода.
        htn.Blackboard.SetValue(NPCBlackboard.NavSmash, false);
        _htn.Replan(htn);
    }

    /// <summary>Живые игроки (с сессией) на карте.</summary>
    /// <summary>
    /// На карте экспедиции мобы не ломают стены: у всех ИИ на карте выключается NavSmash (иначе путь прокладывается
    /// сквозь стену, и моб долбит её вместо обхода). Вызывается один раз в конце генерации карты; мобам волн NavSmash
    /// выключается при появлении (<see cref="PursuePlayerComponent"/>).
    /// </summary>
    public void DisableWallSmashing(EntityUid map)
    {
        var query = EntityQueryEnumerator<HTNComponent, TransformComponent>();
        while (query.MoveNext(out _, out var htn, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            if (htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavSmash, out var smash, EntityManager) && smash)
                htn.Blackboard.SetValue(NPCBlackboard.NavSmash, false);
        }
    }

    public void GetAlivePlayers(EntityUid map, List<(EntityUid Uid, Vector2 Pos)> result)
    {
        result.Clear();
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var state, out var xform))
        {
            if (xform.MapUid != map || HasComp<GhostComponent>(uid) || _mobState.IsDead(uid, state))
                continue;

            result.Add((uid, _xform.GetWorldPosition(xform)));
        }
    }

    /// <summary>Поля расстояний от клеток игроков (по гриду, клетке игрока и тому, открывает ли моб двери).</summary>
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile, bool Doors), (TimeSpan Built, Dictionary<Vector2i, int> Dist)> _fields = new();
    private static readonly TimeSpan FieldLifetime = TimeSpan.FromSeconds(3);

    /// <summary>До игрока ближе этого числа шагов: идём к нему напрямую (короткий путь A* находит всегда).</summary>
    private const int DirectSteps = 16;

    /// <summary>Длина ножки пути по полю расстояний, шагов.</summary>
    private const int LegSteps = 12;

    /// <summary>
    /// Промежуточная точка погони на планете. Основной способ: поле расстояний от игрока по проходимым клеткам
    /// (стены и порода не проходимы, двери проходимы для тех, кто их открывает или вскрывает); моб получает ножку на
    /// <see cref="LegSteps"/> шагов «под горку». Такой путь всегда реальный: обходит комплекс и ведёт через проходы.
    /// Null: идти к игроку напрямую (близко, не планета или моб от игрока отрезан).
    /// </summary>
    private Vector2? ChooseWaypoint(EntityUid uid, PursuePlayerComponent pursue, HTNComponent htn, TransformComponent xform,
        Vector2 pos, Vector2 playerPos)
    {
        // Не планета (карта не грид): идём к игроку напрямую.
        if (xform.GridUid is not { } gridUid || xform.GridUid != xform.MapUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var doors = htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavInteract, out var interact, EntityManager) && interact
                    || htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavPry, out var pry, EntityManager) && pry;
        var playerTile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, playerPos));
        var field = GetField(gridUid, grid, playerTile, doors);

        var mobTile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, pos));
        // Моб отрезан от игрока (поле до него не дошло): идём напрямую, сторож и удаление застрявших разберутся.
        if (!TryNearestInField(field, mobTile, out var current))
            return null;

        // Близко: напрямую (короткий путь находит A*).
        if (field[current] <= DirectSteps)
        {
            pursue.Waypoint = null;
            return null;
        }

        // Текущая ножка ещё актуальна: не дошли и она свежая, а путь к ней находился.
        var now = _timing.CurTime;
        var noPath = TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath;
        if (!noPath && pursue.Waypoint is { } existing && Vector2.DistanceSquared(existing, pos) > 4f
            && now - pursue.WaypointSet < TimeSpan.FromSeconds(4))
        {
            return existing;
        }

        // Под горку по полю на LegSteps шагов; ножка: самая дальняя клетка этого пути, до которой от моба идёт
        // свободная прямая (без стен любого грида), иначе моб по прямой упёрся бы в угол.
        var path = new List<Vector2i>();
        var tile = current;
        for (var i = 0; i < LegSteps; i++)
        {
            var best = tile;
            foreach (var next in new[] { tile + Vector2i.Up, tile + Vector2i.Down, tile + Vector2i.Left, tile + Vector2i.Right })
            {
                if (field.TryGetValue(next, out var d) && d < field[best])
                    best = next;
            }

            if (best == tile)
                break;
            tile = best;
            path.Add(tile);
        }

        // Ножка: самая дальняя клетка пути, до которой от моба свободная прямая (до неё моб идёт к центру клетки, см.
        // LegRange). У угла прямой нет ни до одной: соседняя клетка пути, обойти угол по шагу.
        var target = path.Count > 0 ? path[0] : current;
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (LineInField(gridUid, grid, field, pos, path[i]))
            {
                target = path[i];
                break;
            }
        }

        var leg = _map.GridTileToLocal(gridUid, grid, target).Position;
        pursue.Waypoint = leg;
        pursue.WaypointSet = now;
        return leg;
    }

    private Dictionary<Vector2i, int> GetField(EntityUid gridUid, MapGridComponent grid, Vector2i playerTile, bool doors)
    {
        var key = (gridUid, playerTile, doors);
        var now = _timing.CurTime;
        if (_fields.TryGetValue(key, out var cached) && now - cached.Built < FieldLifetime)
            return cached.Dist;

        var dist = _tiles.DistanceField(gridUid, grid, playerTile, doors);
        _fields[key] = (now, dist);
        return dist;
    }

    /// <summary>Прямая от точки до центра клетки проходит только по клеткам поля (без стен).</summary>
    private bool LineInField(EntityUid gridUid, MapGridComponent grid, Dictionary<Vector2i, int> field, Vector2 from, Vector2i to)
    {
        var end = _map.GridTileToLocal(gridUid, grid, to).Position;
        var length = Vector2.Distance(from, end);
        var samples = Math.Max(1, (int) (length / 0.25f));
        for (var i = 1; i <= samples; i++)
        {
            var point = Vector2.Lerp(from, end, (float) i / samples);
            // Тело моба шириной около 0.7: проверяем и по краям.
            foreach (var side in new[] { Vector2.Zero, new Vector2(0.3f, 0.3f), new Vector2(-0.3f, -0.3f), new Vector2(0.3f, -0.3f), new Vector2(-0.3f, 0.3f) })
            {
                var t = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, point + side));
                if (!field.ContainsKey(t))
                    return false;
            }
        }

        return true;
    }

    /// <returns>True, если моб удалён как застрявший.</returns>
    private void WatchProgress(EntityUid uid, PursuePlayerComponent pursue, HTNComponent htn, Vector2 pos, Vector2 playerPos)
    {
        var now = _timing.CurTime;
        if (pursue.ProgressPosition is not { } last || Vector2.DistanceSquared(last, pos) >= 1f)
        {
            pursue.ProgressPosition = pos;
            pursue.ProgressSince = now;
            return;
        }

        if (now - pursue.ProgressSince < pursue.RepathAfter)
            return;

        // Путь ещё считается (длинный путь в нагруженном сервере считается секундами): не мешаем.
        if (TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Pathfind)
        {
            pursue.ProgressSince = now;
            return;
        }

        // Стоит рядом с игроком (дерётся) или дальник стреляет с дистанции: это не застревание.
        var range = htn.Blackboard.GetValueOrDefault<float>(PursueRangeKey, EntityManager);
        if (Vector2.Distance(pos, playerPos) <= MathF.Max(2.5f, range + 1f))
        {
            pursue.ProgressSince = now;
            return;
        }

        pursue.ProgressSince = now;
        pursue.Waypoint = null;
        pursue.Followed = (null, null); // цель погони будет выставлена и план перестроен заново
        _steering.Unregister(uid);
    }

    /// <summary>Клетка моба в поле или ближайшая к ней (моб может стоять на краю клетки у стены).</summary>
    private static bool TryNearestInField(Dictionary<Vector2i, int> field, Vector2i tile, out Vector2i found)
    {
        found = tile;
        if (field.ContainsKey(tile))
            return true;

        var bestDist = int.MaxValue;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var near = new Vector2i(tile.X + dx, tile.Y + dy);
                if (field.TryGetValue(near, out var d) && d < bestDist)
                {
                    bestDist = d;
                    found = near;
                }
            }
        }

        return bestDist != int.MaxValue;
    }

    private bool CheckStuck(EntityUid uid, PursuePlayerComponent pursue, Vector2 pos, float nearestPlayerSquared)
    {
        var now = _timing.CurTime;
        var near = pursue.PlayerNearDistance * pursue.PlayerNearDistance;

        if (pursue.LastPosition is not { } last
            || Vector2.DistanceSquared(last, pos) >= pursue.StuckDistance * pursue.StuckDistance
            || nearestPlayerSquared <= near)
        {
            pursue.LastPosition = pos;
            pursue.StillSince = now;
            return false;
        }

        if (now - pursue.StillSince < pursue.StuckTime)
            return false;

        Log.Info($"Removing stuck wave mob {ToPrettyString(uid)} at {pos}, nearest player {MathF.Sqrt(nearestPlayerSquared):F0} m");
        QueueDel(uid);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        _players.Clear();
        var playerQuery = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (playerQuery.MoveNext(out var uid, out _, out var state, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || _mobState.IsDead(uid, state) || xform.MapID == MapId.Nullspace)
                continue;

            _players.Add((uid, xform.MapID, _xform.GetWorldPosition(xform)));
        }

        // Старые поля расстояний (игрок ушёл с клетки) выбрасываем.
        foreach (var (key, field) in _fields.ToArray())
        {
            if (_timing.CurTime - field.Built > FieldLifetime * 3)
                _fields.Remove(key);
        }

        var query = EntityQueryEnumerator<PursuePlayerComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pursue, out var htn, out var xform))
        {
            if (_mobState.IsIncapacitated(uid))
                continue;

            var pos = _xform.GetWorldPosition(xform);
            EntityUid? best = null;
            var bestDist = float.MaxValue;
            foreach (var (player, map, playerPos) in _players)
            {
                if (map != xform.MapID)
                    continue;

                var dist = Vector2.DistanceSquared(pos, playerPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = player;
                }
            }

            // Застрявший моб (в щели, за камнем) не двигается: если игроков рядом нет, он удаляется.
            if (!_container.IsEntityInContainer(uid) && CheckStuck(uid, pursue, pos, bestDist))
                continue;

            // Далёкого игрока A* не находит (стоят на месте), поэтому к далёкому идём по ножкам.
            Vector2? waypoint = null;
            if (best is { } far && !_container.IsEntityInContainer(uid))
                waypoint = ChooseWaypoint(uid, pursue, htn, xform, pos, _xform.GetWorldPosition(far));

            // Сторож: моб встал (поиск пути не удался, а путь мог уже освободиться): сбрасываем путь и строим заново.
            if (best is { } watched && !_container.IsEntityInContainer(uid))
                WatchProgress(uid, pursue, htn, pos, _xform.GetWorldPosition(watched));

            // Дальники: стрелять с дистанции можно, только видя игрока; иначе идти вплотную. Ближники: до ближнего боя.
            // К ножке пути: до центра клетки, иначе моб «пришёл» за метр до неё и стоит у угла.
            var range = waypoint != null ? LegRange : ClosingRange;
            var meleeRange = waypoint != null ? LegRange : htn.Blackboard.GetValueOrDefault<float>("MeleeRange", EntityManager);
            if (waypoint == null && best is { } seen)
            {
                var ranged = htn.Blackboard.GetValueOrDefault<float>("RangedRange", EntityManager);
                if (ranged > ClosingRange && _examine.InRangeUnOccluded(uid, seen, ranged))
                    range = ranged;
            }

            var rangeChanged = !MathHelper.CloseTo(htn.Blackboard.GetValueOrDefault<float>(PursueRangeKey, EntityManager), range)
                               || !MathHelper.CloseTo(htn.Blackboard.GetValueOrDefault<float>(PursueMeleeRangeKey, EntityManager), meleeRange);
            if (rangeChanged)
            {
                htn.Blackboard.SetValue(PursueRangeKey, range);
                htn.Blackboard.SetValue(PursueMeleeRangeKey, meleeRange);
            }

            if (best == pursue.Followed.Player && waypoint == pursue.Followed.Waypoint && !rangeChanged)
                continue;

            pursue.Target = best;
            pursue.Followed = (best, waypoint);
            if (waypoint is { } leg && xform.MapUid is { } mapUid)
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(mapUid, leg), htn);
            else if (best is { } target)
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(target, Vector2.Zero), htn);
            else
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

            _htn.Replan(htn);
        }
    }
}
