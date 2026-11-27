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

        // Эвакуация: вместо обычных волн частые маленькие без объявления.
        if (TryComp<ExpeditionExtractionStateComponent>(ent, out var extraction) && extraction.Active)
        {
            UpdateExtractionWaves(ent, expedition);
            return;
        }

        ent.Comp.NextExtractionWave = null;

        if (expedition.EndTime - now < ent.Comp.StopBefore)
            return;

        _pursue.GetAlivePlayers(ent.Owner, _players);
        if (_players.Count == 0)
        {
            // Пока игроков нет, волн нет; таймер первой волны запустится, когда они появятся.
            ent.Comp.NextWave = null;
            return;
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

    /// <summary>
    /// Принудительно спавнит волну на карте экспедиции прямо сейчас (команда expeditionwave). Следующая волна по таймеру
    /// отсчитывается заново.
    /// </summary>
    /// <returns>Сколько мобов упало; отрицательное число: это не карта планетной экспедиции; 0: волна не вышла (нет игроков и т. п.).</returns>
    public int ForceWave(EntityUid map)
    {
        if (!TryComp<SalvageExpeditionComponent>(map, out var expedition) || !expedition.Planet.IsValid())
            return -1;

        var waves = EnsureComp<ExpeditionWavesComponent>(map);
        _pursue.GetAlivePlayers(map, _players);
        if (_players.Count == 0)
            return 0;

        waves.NextWave = _timing.CurTime + Delay(waves.IntervalMin, waves.IntervalMax);
        return SpawnWave((map, waves), expedition);
    }

    private void UpdateExtractionWaves(Entity<ExpeditionWavesComponent> ent, SalvageExpeditionComponent expedition)
    {
        var now = _timing.CurTime;
        if (expedition.EndTime - now < ent.Comp.ExtractionStopBefore)
            return;

        _pursue.GetAlivePlayers(ent.Owner, _players);
        if (_players.Count == 0)
            return;

        // Первая эвакуационная волна тоже через 20-40 с после вызова шаттла.
        if (ent.Comp.NextExtractionWave is not { } next)
        {
            ent.Comp.NextExtractionWave = now + Delay(ent.Comp.ExtractionIntervalMin, ent.Comp.ExtractionIntervalMax);
            return;
        }

        if (now < next)
            return;

        ent.Comp.NextExtractionWave = now + Delay(ent.Comp.ExtractionIntervalMin, ent.Comp.ExtractionIntervalMax);
        SpawnWave(ent, expedition, ent.Comp.ExtractionBudgetFraction, quiet: true);
    }

    private TimeSpan Delay(float min, float max) => TimeSpan.FromSeconds(_random.NextFloat(min, max));

    /// <summary>Мобы волны, которые ещё в строю: не убиты и не в крите (для музыки, 90% выбыло: волна закончилась).</summary>
    /// <summary>
    /// Сбросить сущность в дроп-поде (как мобов волны) на свободный открытый тайл рядом с точкой. Сущность выходит из
    /// пода при посадке. Возвращает false, если места или пода нет (тогда сущность ставится прямо в точку).
    /// </summary>
    public bool DropCargo(EntityUid map, Vector2 near, EntityUid cargo)
    {
        var coords = _rocks.FindDropTile(map, near, 3f, 7f, _random) ?? new EntityCoordinates(map, near);
        var pod = Spawn(PodProto, coords);
        if (!_container.TryGetContainer(pod, PodContainer, out var container) || !_container.Insert(cargo, container))
        {
            QueueDel(pod);
            _xform.SetCoordinates(cargo, coords);
            return false;
        }

        _trigger.Trigger(pod);
        return true;
    }

    public int CountFightingWaveMobs(EntityUid map)
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
    /// <param name="budgetFraction">Доля mobBudget; null: обычная случайная (<see cref="ExpeditionWavesComponent.BudgetFractionMin"/>..Max).</param>
    /// <param name="quiet">Без объявления и без боевой музыки (эвакуационные волны).</param>
    public int SpawnWave(Entity<ExpeditionWavesComponent> ent, SalvageExpeditionComponent expedition,
        float? budgetFraction = null, bool quiet = false)
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
            difficulty.MobBudget * (budgetFraction ?? _random.NextFloat(ent.Comp.BudgetFractionMin, ent.Comp.BudgetFractionMax)),
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

        if (spawned > 0 && !quiet)
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

    /// <summary>Один дроп-под с группой мобов: падает рядом с игроком, мобы выходят из него на земле.</summary>
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

    /// <summary>
    /// Место падения пода: пол данжа рядом со случайным живым игроком. Если рядом данжа нет (игрок снаружи), любой
    /// свободный тайл из открытой области рядом с ним.
    /// </summary>
    private EntityCoordinates? FindDropTile(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid)
    {
        var center = _random.Pick(_players).Pos;
        return _rocks.FindDungeonDropTile(grid, center, ent.Comp.DropRingMin, ent.Comp.DropRingMax, _random)
               ?? _rocks.FindDropTile(grid, center, ent.Comp.DropRingMin, ent.Comp.DropRingMax, _random);
    }
}
