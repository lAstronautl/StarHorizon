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
    [Dependency] private readonly RockClearSystem _rocks = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

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
            && (now >= waveState.End || CountFightingWaveMobs(ent.Owner) <= ent.Comp.WaveTotal * ent.Comp.MusicOffFraction))
        {
            waveState.Active = false;
            ent.Comp.WaveTotal = 0;
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
        if (ent.Comp.DropAnchor == null)
        {
            ent.Comp.DropAnchor = _players[0].Pos;

            // Земля вокруг места падения остаётся подгруженной, даже когда игроки ушли далеко.
            if (TryComp<MapGridComponent>(ent, out _))
            {
                var loader = Spawn(null, new EntityCoordinates(ent.Owner, _players[0].Pos));
                AddComp<Content.Server.Parallax.BiomeForceLoadComponent>(loader);
                ent.Comp.AnchorLoader = loader;
            }
        }

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

    /// <summary>Мобы волны, которые ещё в строю: не убиты и не в крите (для музыки, 90% выбыло: волна закончилась).</summary>
    private int CountFightingWaveMobs(EntityUid map)
    {
        var fighting = 0;
        var query = EntityQueryEnumerator<PursuePlayerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            // Без MobState (турели, строения) моба убить нельзя: в счёт волны не входят.
            if (!TryComp<MobStateComponent>(uid, out var state) || !_mobState.IsAlive(uid, state))
                continue;

            fighting++;
        }

        return fighting;
    }

    private int CountAliveWaveMobs(EntityUid map)
    {
        var alive = 0;
        var query = EntityQueryEnumerator<PursuePlayerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            if (!TryComp<MobStateComponent>(uid, out var state) || _mobState.IsDead(uid, state))
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
        {
            Log.Warning($"Expedition wave: no grid or no players ({_players.Count}) on {ToPrettyString(ent.Owner)}");
            return 0;
        }

        var alive = CountAliveWaveMobs(ent.Owner);
        if (alive >= ent.Comp.MaxAliveWaveMobs)
        {
            Log.Warning($"Expedition wave: {alive} wave mobs alive already, skipping");
            return 0;
        }

        var missionParams = expedition.MissionParams;
        if (!_proto.TryIndex<SalvageDifficultyPrototype>(missionParams.Difficulty, out var difficulty))
        {
            Log.Warning($"Expedition wave: unknown difficulty {missionParams.Difficulty}");
            return 0;
        }

        string factionId;
        try
        {
            factionId = _salvage.GetMission(missionParams.MissionType, difficulty, missionParams.Seed, missionParams.Biome).Faction;
        }
        catch (InvalidOperationException e)
        {
            Log.Warning($"Expedition wave: could not roll the mission of {ToPrettyString(ent.Owner)}: {e.Message}");
            return 0;
        }

        if (!_proto.TryIndex<SalvageFactionPrototype>(factionId, out var faction))
        {
            Log.Warning($"Expedition wave: unknown faction {factionId}");
            return 0;
        }

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

        if (protos.Count == 0)
            Log.Warning($"Expedition wave: budget {budget} bought no mobs from faction {factionId} (entries {budgetEntries.Count})");

        Log.Info($"Expedition wave: {protos.Count} mobs from {factionId} (budget fraction {budget})");
        var spawned = 0;
        var counted = 0;
        for (var i = 0; i < protos.Count; i += ent.Comp.MobsPerPod)
        {
            var group = protos.GetRange(i, Math.Min(ent.Comp.MobsPerPod, protos.Count - i));
            if (DropPod(ent, (ent.Owner, gridComp), group) is { } fighters)
            {
                spawned += group.Count;
                counted += fighters;
            }
        }

        if (spawned > 0)
        {
            ent.Comp.WaveTotal += counted;
            var state = EnsureComp<ExpeditionWaveStateComponent>(ent.Owner);
            state.Active = true;
            state.End = _timing.CurTime + ent.Comp.WaveMusicDuration;
            Dirty(ent.Owner, state);

            _salvage.Announce(ent.Owner, Loc.GetString("expedition-wave-announcement"));
        }

        return spawned;
    }

    /// <summary>Один дроп-под с группой мобов: падает у якоря, мобы выходят из него на земле.</summary>
    /// <returns>Сколько в поде мобов, которых можно убить (с MobState); null, если под не упал.</returns>
    private int? DropPod(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid, List<string> mobs)
    {
        if (FindDropTile(ent, grid) is not { } coords)
        {
            Log.Warning($"Expedition wave: no free tile for a drop pod on {ToPrettyString(ent.Owner)}");
            return null;
        }

        var pod = Spawn(PodProto, coords);
        if (!_container.TryGetContainer(pod, PodContainer, out var container))
        {
            Log.Warning($"Expedition wave: drop pod {ToPrettyString(pod)} has no container {PodContainer}");
            QueueDel(pod);
            return null;
        }

        var fighters = 0;
        foreach (var proto in mobs)
        {
            foreach (var mob in SpawnWaveMobs(proto, coords))
            {
                if (HasComp<MobStateComponent>(mob))
                    fighters++;

                RemComp<GhostRoleComponent>(mob);
                RemComp<GhostTakeoverAvailableComponent>(mob);
                EnsureComp<PursuePlayerComponent>(mob);
                _container.Insert(mob, container);
            }
        }

        _trigger.Trigger(pod);
        return fighters;
    }

    /// <summary>
    /// Спавнит запись фракции и возвращает настоящих мобов. Многие записи фракций (SpawnMobSyndicate..., SpawnMob...)
    /// не мобы, а спавнеры: при создании они сами порождают моба и удаляются. Раньше волна вешала погоню на спавнер,
    /// и настоящие мобы появлялись сразу, без погони и не в поде.
    /// </summary>
    public List<EntityUid> SpawnWaveMobs(string proto, EntityCoordinates coords)
    {
        var mapPos = _xform.ToMapCoordinates(coords);
        var before = new HashSet<EntityUid>(_lookup.GetEntitiesInRange<MobStateComponent>(mapPos, 4f).Select(e => e.Owner));

        var spawned = SpawnAtPosition(proto, coords);
        if (HasComp<MobStateComponent>(spawned))
            return new List<EntityUid> { spawned };

        // Спавнер: настоящие мобы появились рядом за время создания.
        var result = new List<EntityUid>();
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(mapPos, 4f))
        {
            if (!before.Contains(mob.Owner) && !TerminatingOrDeleted(mob.Owner))
                result.Add(mob.Owner);
        }

        // Ничего не появилось: сама запись остаётся (строение, турель), чтобы не терять её из волны.
        if (result.Count == 0 && !TerminatingOrDeleted(spawned))
            result.Add(spawned);

        return result;
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
        // Свободный тайл без породы из открытой области: не в замурованной щели.
        return _rocks.FindDropTile(grid, center, ent.Comp.DropRingMin, ent.Comp.DropRingMax, _random);
    }
}
