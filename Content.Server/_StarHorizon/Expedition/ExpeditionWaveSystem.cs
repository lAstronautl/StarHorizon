using System.Linq;
using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Containers;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Random;
using Content.Shared.Salvage.Expeditions;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Волны врагов на планетной экспедиции: через случайные интервалы тратится случайная доля mobBudget сложности,
/// мобы фракции задания появляются кольцом вокруг игроков и сразу идут к ним (<see cref="PursuePlayerComponent"/>).
/// </summary>
public sealed class ExpeditionWaveSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SalvageSystem _salvage = default!;
    [Dependency] private readonly RandomSystem _randomSys = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly PursuePlayerSystem _pursue = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public const string PodProto = "DropPodspawner";
    public const string PodContainer = "clowncar_container";

    private TimeSpan _nextUpdate;
    private readonly List<(EntityUid Uid, Vector2 Pos)> _players = new();

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var expedition))
        {
            if (!expedition.Planet.IsValid())
                continue;

            var waves = EnsureComp<ExpeditionWavesComponent>(uid);
            UpdateExpedition((uid, waves), expedition);
        }
    }

    private void UpdateExpedition(Entity<ExpeditionWavesComponent> ent, SalvageExpeditionComponent expedition)
    {
        var now = _timing.CurTime;

        // Клиентам: волна закончилась по истечении времени или когда все мобы волны убиты.
        if (TryComp<ExpeditionWaveStateComponent>(ent, out var waveState) && waveState.Active
            && (now >= waveState.End || CountAliveWaveMobs(ent.Owner) == 0))
        {
            waveState.Active = false;
            Dirty(ent.Owner, waveState);
        }

        if (expedition.EndTime - now < ent.Comp.StopBefore)
            return;

        _pursue.GetAlivePlayers(ent.Owner, _players);
        if (_players.Count == 0)
        {
            // Пока игроков нет, волн нет; таймер первой волны запустится, когда они появятся.
            ent.Comp.NextWave = null;
            return;
        }

        // Место падения: где начал первый игрок.
        ent.Comp.DropAnchor ??= _players[0].Pos;

        if (ent.Comp.NextWave is not { } next)
        {
            ent.Comp.NextWave = now + Delay(ent.Comp.FirstWaveMin, ent.Comp.FirstWaveMax);
            return;
        }

        if (now < next)
            return;

        ent.Comp.NextWave = now + Delay(ent.Comp.IntervalMin, ent.Comp.IntervalMax);
        SpawnWave(ent, expedition);
    }

    private TimeSpan Delay(float min, float max) => TimeSpan.FromSeconds(_random.NextFloat(min, max));

    private int CountAliveWaveMobs(EntityUid map)
    {
        var alive = 0;
        var query = EntityQueryEnumerator<PursuePlayerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            if (TryComp<MobStateComponent>(uid, out var state) && _mobState.IsDead(uid, state))
                continue;

            alive++;
        }

        return alive;
    }

    /// <summary>
    /// Спавнит волну: тратит бюджет сложности целиком, мобы падают группами в дроп-подах Goobstation (DropPodspawner)
    /// у места, где начал первый игрок. Возвращает число мобов.
    /// </summary>
    public int SpawnWave(Entity<ExpeditionWavesComponent> ent, SalvageExpeditionComponent expedition)
    {
        if (!TryComp<MapGridComponent>(ent, out var gridComp) || _players.Count == 0)
            return 0;

        var alive = CountAliveWaveMobs(ent.Owner);
        if (alive >= ent.Comp.MaxAliveWaveMobs)
            return 0;

        var missionParams = expedition.MissionParams;
        if (!_proto.TryIndex<SalvageDifficultyPrototype>(missionParams.Difficulty, out var difficulty))
            return 0;

        string factionId;
        try
        {
            factionId = _salvage.GetMission(missionParams.MissionType, difficulty, missionParams.Seed, missionParams.Biome).Faction;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }

        if (!_proto.TryIndex<SalvageFactionPrototype>(factionId, out var faction))
            return 0;

        var budgetEntries = new List<IBudgetEntry>();
        foreach (var entry in faction.MobGroups)
            budgetEntries.Add(entry);

        var budget = MathF.Min(
            difficulty.MobBudget * _random.NextFloat(ent.Comp.BudgetFractionMin, ent.Comp.BudgetFractionMax),
            ent.Comp.MaxWaveBudget);
        var probSum = budgetEntries.Sum(x => x.Prob);
        var random = new Random(_random.Next());
        var protos = new List<string>();

        while (budget > 0f && alive + protos.Count < ent.Comp.MaxAliveWaveMobs)
        {
            var entry = _randomSys.GetBudgetEntry(ref budget, ref probSum, budgetEntries, random);
            if (entry == null)
                break;

            protos.Add(entry.Proto);
        }

        var spawned = 0;
        for (var i = 0; i < protos.Count; i += ent.Comp.MobsPerPod)
        {
            var group = protos.GetRange(i, Math.Min(ent.Comp.MobsPerPod, protos.Count - i));
            if (DropPod(ent, (ent.Owner, gridComp), group))
                spawned += group.Count;
        }

        if (spawned > 0)
        {
            var state = EnsureComp<ExpeditionWaveStateComponent>(ent.Owner);
            state.Active = true;
            state.End = _timing.CurTime + ent.Comp.WaveMusicDuration;
            Dirty(ent.Owner, state);

            _salvage.Announce(ent.Owner, Loc.GetString("expedition-wave-announcement"));
        }

        return spawned;
    }

    /// <summary>Один дроп-под с группой мобов: падает у якоря, мобы выходят из него на земле.</summary>
    private bool DropPod(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid, List<string> mobs)
    {
        if (FindDropTile(ent, grid) is not { } coords)
            return false;

        var pod = Spawn(PodProto, coords);
        if (!_container.TryGetContainer(pod, PodContainer, out var container))
        {
            QueueDel(pod);
            return false;
        }

        foreach (var proto in mobs)
        {
            var mob = SpawnAtPosition(proto, coords);
            RemComp<GhostRoleComponent>(mob);
            RemComp<GhostTakeoverAvailableComponent>(mob);
            EnsureComp<PursuePlayerComponent>(mob);
            _container.Insert(mob, container);
        }

        _trigger.Trigger(pod);
        return true;
    }

    /// <summary>Свободный тайл в кольце вокруг якоря; нет подгруженной земли у якоря: в кольце вокруг ближайшего игрока.</summary>
    private EntityCoordinates? FindDropTile(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid)
    {
        if (ent.Comp.DropAnchor is { } anchor && TryRingTile(ent, grid, anchor) is { } near)
            return near;

        // Запасной вариант: игрок рядом, значит земля вокруг него подгружена.
        var center = _random.Pick(_players).Pos;
        return TryRingTile(ent, grid, center);
    }

    private EntityCoordinates? TryRingTile(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid, Vector2 center)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var pos = center + _random.NextAngle().ToVec() * _random.NextFloat(ent.Comp.DropRingMin, ent.Comp.DropRingMax);
            var coords = new EntityCoordinates(grid, pos);

            if (!_map.TryGetTileRef(grid, grid.Comp, coords, out var tile) || tile.Tile.IsEmpty)
                continue;

            if (!_anchorable.TileFree(grid, tile.GridIndices, (int) CollisionGroup.MachineLayer, (int) CollisionGroup.MachineLayer))
                continue;

            return _map.GridTileToLocal(grid, grid.Comp, tile.GridIndices);
        }

        return null;
    }
}
