using System.Numerics;
using Content.Server.NPC;
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
    [Dependency] private readonly Robust.Shared.Containers.SharedContainerSystem _container = default!;

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

            if (best == pursue.Target)
                continue;

            pursue.Target = best;
            if (best is { } target)
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(target, Vector2.Zero), htn);
            else
                htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);

            _htn.Replan(htn);
        }
    }
}
