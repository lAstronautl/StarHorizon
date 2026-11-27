using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Hands;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Задание «Чёрный ящик» (<see cref="Content.Shared.Salvage.SalvageMissionType.Retrieval"/>): ящик лежит в самой
/// дальней комнате данжа. Кто его поднял, тот выдал себя: сигнал засекли и сразу приходит волна врагов. Задание
/// выполнено, когда ящик на борту эвакуационного шаттла (в руках или на полу шаттла).
/// </summary>
[RegisterComponent]
public sealed partial class SalvageRetrievalExpeditionComponent : Component
{
    [ViewVariables] public EntityUid? Box;

    /// <summary>Ящик уже поднимали (волна вызвана).</summary>
    [ViewVariables] public bool Found;

    /// <summary>Ящик на борту эвакуационного шаттла.</summary>
    [ViewVariables] public bool Delivered;

    /// <summary>Ящик пропал (уничтожен): объявили один раз.</summary>
    [ViewVariables] public bool LostAnnounced;
}

/// <summary>Чёрный ящик экспедиции: к какой карте экспедиции он относится.</summary>
[RegisterComponent]
public sealed partial class ExpeditionBlackBoxComponent : Component
{
    [ViewVariables] public EntityUid Expedition;
}

public sealed class ExpeditionBlackBoxSystem : EntitySystem
{
    public const string BlackBoxProto = "ExpeditionBlackBox";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SalvageSystem _salvage = default!;
    [Dependency] private readonly ExpeditionWaveSystem _waves = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionBlackBoxComponent, GotEquippedHandEvent>(OnPickedUp);
    }

    private void OnPickedUp(Entity<ExpeditionBlackBoxComponent> ent, ref GotEquippedHandEvent args)
    {
        var map = ent.Comp.Expedition;
        if (!TryComp<SalvageRetrievalExpeditionComponent>(map, out var retrieval) || retrieval.Found)
            return;

        retrieval.Found = true;
        _salvage.Announce(map, Loc.GetString("expedition-blackbox-found"));

        // Сигнал ящика засекли: волна приходит сразу (следующая по таймеру отсчитывается заново).
        _waves.ForceWave(map);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<SalvageRetrievalExpeditionComponent, SalvageExpeditionComponent>();
        while (query.MoveNext(out var map, out var retrieval, out var expedition))
        {
            if (retrieval.Delivered)
                continue;

            if (retrieval.Box is not { } box || TerminatingOrDeleted(box))
            {
                if (!retrieval.LostAnnounced && retrieval.Box != null)
                {
                    retrieval.LostAnnounced = true;
                    _salvage.Announce(map, Loc.GetString("expedition-blackbox-lost"));
                }

                continue;
            }

            if (!IsAboardExtractionShuttle(map, box))
                continue;

            retrieval.Delivered = true;
            expedition.Completed = true;
            _salvage.Announce(map, Loc.GetString("expedition-blackbox-delivered"));
        }
    }

    /// <summary>Ящик на гриде эвакуационного шаттла этой экспедиции (на полу или у кого-то в руках на борту).</summary>
    public bool IsAboardExtractionShuttle(EntityUid map, EntityUid box)
    {
        return TryComp<ExpeditionExtractionComponent>(map, out var extraction)
               && extraction.Shuttle is { } shuttle
               && Transform(box).GridUid == shuttle;
    }
}
