using System.Linq;
using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._StarHorizon.Expedition;
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

        // Клиентам: волна закончилась по истечении времени.
        if (TryComp<ExpeditionWaveStateComponent>(ent, out var waveState) && waveState.Active && now >= waveState.End)
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

    /// <summary>Спавнит волну: тратит случайную долю mobBudget сложности, возвращает потраченное число мобов.</summary>
    public int SpawnWave(Entity<ExpeditionWavesComponent> ent, SalvageExpeditionComponent expedition)
    {
        if (!TryComp<MapGridComponent>(ent, out var gridComp) || _players.Count == 0)
            return 0;

        var alive = 0;
        var pursuers = EntityQueryEnumerator<PursuePlayerComponent, TransformComponent>();
        while (pursuers.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == ent.Owner)
                alive++;
        }

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
        var spawned = 0;

        while (budget > 0f && alive + spawned < ent.Comp.MaxAliveWaveMobs)
        {
            var entry = _randomSys.GetBudgetEntry(ref budget, ref probSum, budgetEntries, random);
            if (entry == null)
                break;

            if (SpawnAroundPlayer(ent, (ent.Owner, gridComp), entry.Proto))
                spawned++;
        }

        if (spawned > 0)
        {
            var state = EnsureComp<ExpeditionWaveStateComponent>(ent.Owner);
            state.Active = true;
            state.End = _timing.CurTime + ent.Comp.WaveMusicDuration;
            Dirty(ent.Owner, state);
        }

        if (spawned > 0)
            _salvage.Announce(ent.Owner, Loc.GetString("expedition-wave-announcement"));

        return spawned;
    }

    private bool SpawnAroundPlayer(Entity<ExpeditionWavesComponent> ent, Entity<MapGridComponent> grid, string proto)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var (_, center) = _random.Pick(_players);
            var pos = center + _random.NextAngle().ToVec() * _random.NextFloat(ent.Comp.RingMin, ent.Comp.RingMax);
            var coords = new EntityCoordinates(grid, pos);

            if (!_map.TryGetTileRef(grid, grid.Comp, coords, out var tile) || tile.Tile.IsEmpty)
                continue;

            if (!_anchorable.TileFree(grid, tile.GridIndices, (int) CollisionGroup.MachineLayer, (int) CollisionGroup.MachineLayer))
                continue;

            var mob = SpawnAtPosition(proto, _map.GridTileToLocal(grid, grid.Comp, tile.GridIndices));
            RemComp<GhostRoleComponent>(mob);
            RemComp<GhostTakeoverAvailableComponent>(mob);
            EnsureComp<PursuePlayerComponent>(mob);
            return true;
        }

        return false;
    }
}
