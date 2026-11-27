using System.Linq;
using Content.Server._NF.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions;
using Content.Server._NF.Salvage.Expeditions.Structure;
using Content.Shared.Shuttles.Components;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Собирает данные интерфейса планетной экспедиции (<see cref="ExpeditionHudComponent"/>): задачи с прогрессом,
/// время до эвакуации и до вылета шаттла, волну врагов и место посадки эвакуационного шаттла.
/// </summary>
public sealed class ExpeditionHudSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly Robust.Shared.Prototypes.IPrototypeManager _proto = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly ExpeditionWaveSystem _waves = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);
    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + UpdateInterval;

        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var expedition))
        {
            if (!expedition.Planet.IsValid())
                continue;

            UpdateHud(uid, expedition);
        }
    }

    /// <summary>Обновить данные интерфейса карты экспедиции сейчас (для тестов и команд).</summary>
    public void UpdateHud(EntityUid map, SalvageExpeditionComponent expedition)
    {
        var hud = EnsureComp<ExpeditionHudComponent>(map);
        var dirty = false;

        void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            dirty = true;
        }

        Set(ref hud.EndTime, expedition.EndTime);

        var arriveBefore = TryComp<ExpeditionExtractionComponent>(map, out var extraction)
            ? extraction.ArriveBefore
            : new ExpeditionExtractionComponent().ArriveBefore;
        Set(ref hud.ExtractionAt, expedition.EndTime - arriveBefore);

        // Задачи: сколько целей было (запоминаем максимум) и сколько уже выполнено.
        var objectives = new List<ExpeditionObjective>();
        if (TryComp<SalvageDestructionExpeditionComponent>(map, out var destruction))
            objectives.Add(Objective(hud, ExpeditionObjectiveKind.Destroy, destruction.Structures));
        if (TryComp<SalvageEliminationExpeditionComponent>(map, out var elimination))
            objectives.Add(Objective(hud, ExpeditionObjectiveKind.Eliminate, elimination.Megafauna));

        if (TryComp<SalvageRetrievalExpeditionComponent>(map, out var retrieval))
        {
            var name = _proto.Index<Robust.Shared.Prototypes.EntityPrototype>(ExpeditionBlackBoxSystem.BlackBoxProto).Name;
            if (retrieval.Box is { } box && TryComp(box, out MetaDataComponent? boxMeta))
                name = boxMeta.EntityName;

            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Find, Name = name, Total = 1, Done = retrieval.Found ? 1 : 0,
            });
            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Deliver, Name = name, Total = 1, Done = retrieval.Delivered ? 1 : 0,
            });
        }

        if (TryComp<SalvageMiningExpeditionComponent>(map, out var mining))
        {
            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Quota, Name = Loc.GetString("expedition-hud-ore"),
                Total = mining.Quota, Done = Math.Min(mining.Collected, mining.Quota),
            });
        }

        if (TryComp<SalvageSamplingExpeditionComponent>(map, out var sampling))
        {
            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Samples,
                Name = _proto.Index<Robust.Shared.Prototypes.EntityPrototype>(ExpeditionObjectivesSystem.SampleProto).Name,
                Total = sampling.Required, Done = Math.Min(sampling.Collected, sampling.Required),
            });
        }

        if (TryComp<SalvageRepairExpeditionComponent>(map, out var repair))
        {
            var repaired = repair.Points.Count(p => !Exists(p) || Comp<ExpeditionRepairPointComponent>(p).Repaired);
            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Repair, Total = repair.Total,
                Done = repair.Stage == ExpeditionRepairStage.Repairing ? repaired : repair.Total,
            });

            var defenseTotal = (int) repair.DefenseDuration.TotalSeconds;
            var defenseDone = repair.Stage switch
            {
                ExpeditionRepairStage.Repairing => 0,
                ExpeditionRepairStage.Defending => Math.Clamp((int) (_timing.CurTime - repair.DefenseStart).TotalSeconds, 0, defenseTotal),
                _ => defenseTotal,
            };
            objectives.Add(new ExpeditionObjective
            {
                Kind = ExpeditionObjectiveKind.Defend, Total = defenseTotal, Done = defenseDone,
            });
        }

        if (!objectives.SequenceEqual(hud.Objectives))
        {
            hud.Objectives = objectives;
            dirty = true;
        }

        Set(ref hud.Completed, expedition.Completed);

        // Волна: сколько её мобов в строю (для строки «Волна врагов»).
        var fighting = 0;
        var total = 0;
        if (TryComp<ExpeditionWaveStateComponent>(map, out var waveState) && waveState.Active
            && TryComp<ExpeditionWavesComponent>(map, out var waves))
        {
            fighting = _waves.CountFightingWaveMobs(map);
            total = waves.WaveTotal;
        }

        Set(ref hud.WaveFighting, fighting);
        Set(ref hud.WaveTotal, total);

        // Эвакуационный шаттл: сел ли на карту и где стоит.
        NetEntity? shuttleNet = null;
        var landed = false;
        var position = hud.ShuttlePosition;
        if (extraction?.Shuttle is { } shuttle && Exists(shuttle))
        {
            shuttleNet = GetNetEntity(shuttle);
            var xform = Transform(shuttle);
            landed = xform.MapUid == map
                     && (!TryComp<FTLComponent>(shuttle, out var ftl) || ftl.State is FTLState.Cooldown or FTLState.Available);
            if (landed)
                position = _xform.GetWorldPosition(xform);
        }

        Set(ref hud.Shuttle, shuttleNet);
        Set(ref hud.ShuttleLanded, landed);
        Set(ref hud.ShuttlePosition, position);

        if (dirty)
            Dirty(map, hud);
    }

    private ExpeditionObjective Objective(ExpeditionHudComponent hud, ExpeditionObjectiveKind kind, List<EntityUid> targets)
    {
        var previous = hud.Objectives.FirstOrDefault(o => o.Kind == kind);
        var name = previous?.Name ?? string.Empty;
        foreach (var target in targets)
        {
            if (TryComp(target, out MetaDataComponent? meta))
            {
                name = meta.EntityName;
                break;
            }
        }

        var remaining = targets.Count;
        var total = Math.Max(previous?.Total ?? 0, remaining);
        return new ExpeditionObjective { Kind = kind, Name = name, Total = total, Done = total - remaining };
    }
}
