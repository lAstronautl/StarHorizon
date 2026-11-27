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
    [Dependency] private readonly RockClearSystem _rocks = default!;
    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _container = default!;

    /// <summary>
    /// До какой дистанции дальники идут к цели погони. Если игрок в прямой видимости, это дальность стрельбы
    /// (RangedRange); если его не видно (за стеной) или впереди промежуточная точка пути, подходят вплотную,
    /// иначе встали бы в 10 м от игрока за стеной и не стреляли.
    /// </summary>
    public const string PursueRangeKey = "PursueRange";
    private const float ClosingRange = 1.5f;

    [Dependency] private readonly Content.Shared.Examine.ExamineSystemShared _examine = default!;

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
        // Стены в погоне не ломаем: иначе путь прокладывается сквозь стену, и моб (ксенос) долбит её вместо обхода.
        htn.Blackboard.SetValue(NPCBlackboard.NavSmash, false);
        _htn.Replan(htn);
    }

    /// <summary>Живые игроки (с сессией) на карте.</summary>
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

    /// <summary>
    /// Цель ближайшей ножки пути к далёкому игроку: открытая земля на <see cref="PursuePlayerComponent.LegLength"/> метров
    /// вперёд (с обходом вправо и влево, если прямо порода). Null: игрок близко или ножку выбрать не вышло, тогда идём к игроку.
    /// </summary>
    private Vector2? ChooseWaypoint(EntityUid uid, PursuePlayerComponent pursue, TransformComponent xform, Vector2 pos, Vector2 playerPos)
    {
        var toPlayer = playerPos - pos;
        if (toPlayer.Length() <= pursue.DirectRange || xform.GridUid is not { } grid || xform.GridUid != xform.MapUid)
        {
            pursue.Waypoint = null;
            return null;
        }

        var now = _timing.CurTime;

        // К выбранной ножке пути нет (A* сдался): в следующий раз пробуем другой угол.
        var noPath = TryComp<NPCSteeringComponent>(uid, out var steering) && steering.Status == SteeringStatus.NoPath;
        if (noPath)
            pursue.AngleStart++;

        if (!noPath
            && pursue.Waypoint is { } current
            && Vector2.DistanceSquared(current, pos) > 9f
            && now - pursue.WaypointSet < TimeSpan.FromSeconds(8))
        {
            return current;
        }

        var dir = Vector2.Normalize(toPlayer);
        var angles = new[] { 0f, 35f, -35f, 70f, -70f, 105f, -105f };
        for (var n = 0; n < angles.Length; n++)
        {
            var degrees = angles[(n + pursue.AngleStart) % angles.Length];
            var rotated = new Angle(Angle.FromDegrees(degrees).Theta + dir.ToAngle().Theta).ToVec();
            if (_rocks.FindOpenNear(grid, pos + rotated * pursue.LegLength) is { } open)
            {
                pursue.Waypoint = open;
                pursue.WaypointSet = now;
                return open;
            }
        }

        pursue.Waypoint = null;
        return null;
    }

    /// <returns>True, если моб удалён как застрявший.</returns>
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
                waypoint = ChooseWaypoint(uid, pursue, xform, pos, _xform.GetWorldPosition(far));

            // Дальники: стрелять с дистанции можно, только видя игрока; иначе идти вплотную.
            var range = ClosingRange;
            if (waypoint == null && best is { } seen)
            {
                var ranged = htn.Blackboard.GetValueOrDefault<float>("RangedRange", EntityManager);
                if (ranged > ClosingRange && _examine.InRangeUnOccluded(uid, seen, ranged))
                    range = ranged;
            }

            var rangeChanged = !MathHelper.CloseTo(htn.Blackboard.GetValueOrDefault<float>(PursueRangeKey, EntityManager), range);
            if (rangeChanged)
                htn.Blackboard.SetValue(PursueRangeKey, range);

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
