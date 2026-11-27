using System.Numerics;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Задание «Чёрный ящик» (<see cref="Content.Shared.Salvage.SalvageMissionType.Retrieval"/>), как аплинк в DRG: ящик
/// стоит в самой дальней комнате данжа. Его активируют, и начинается скачивание данных: оно идёт, только пока в зоне
/// вокруг ящика есть живой игрок, а сигнал ящика всё это время привлекает волны врагов.
/// </summary>
[RegisterComponent]
public sealed partial class SalvageRetrievalExpeditionComponent : Component
{
    [ViewVariables] public EntityUid? Box;

    /// <summary>Ящик активирован, скачивание началось.</summary>
    [ViewVariables] public bool Started;

    /// <summary>Данные скачаны.</summary>
    [ViewVariables] public bool Done;

    /// <summary>Сколько уже скачано (время в зоне).</summary>
    [ViewVariables] public TimeSpan Progress;

    /// <summary>Сколько надо простоять в зоне, чтобы скачать всё.</summary>
    [DataField] public TimeSpan Duration = TimeSpan.FromMinutes(2);

    [DataField] public TimeSpan WaveInterval = TimeSpan.FromSeconds(40);
    [ViewVariables] public TimeSpan NextWave;

    /// <summary>Сейчас в зоне никого (скачивание на паузе).</summary>
    [ViewVariables] public bool Paused;
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
    [Dependency] private readonly PursuePlayerSystem _pursue = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);
    private readonly List<(EntityUid Uid, Vector2 Pos)> _players = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionBlackBoxComponent, InteractHandEvent>(OnInteract);
    }

    private void OnInteract(Entity<ExpeditionBlackBoxComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!Activate(ent))
            _popup.PopupEntity(Loc.GetString("expedition-blackbox-busy"), ent, args.User);
    }

    /// <summary>Активировать ящик: начинается скачивание данных и сразу приходит волна. false: уже активирован.</summary>
    public bool Activate(Entity<ExpeditionBlackBoxComponent> ent)
    {
        var map = ent.Comp.Expedition;
        if (!TryComp<SalvageRetrievalExpeditionComponent>(map, out var retrieval) || retrieval.Started)
            return false;

        retrieval.Started = true;
        retrieval.NextWave = _timing.CurTime + retrieval.WaveInterval;

        var zone = EnsureComp<ExpeditionBlackBoxZoneComponent>(ent);
        zone.Active = true;
        Dirty(ent, zone);

        _salvage.Announce(map, Loc.GetString("expedition-blackbox-started",
            ("seconds", (int) retrieval.Duration.TotalSeconds), ("radius", (int) zone.Radius)));
        _waves.ForceWave(map);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + UpdateInterval;

        var query = EntityQueryEnumerator<SalvageRetrievalExpeditionComponent, SalvageExpeditionComponent>();
        while (query.MoveNext(out var map, out var retrieval, out var expedition))
        {
            if (!retrieval.Started || retrieval.Done || retrieval.Box is not { } box || !TryComp<ExpeditionBlackBoxZoneComponent>(box, out var zone))
                continue;

            // Скачивание идёт, только пока в зоне есть живой игрок.
            var boxPos = _xform.GetWorldPosition(box);
            _pursue.GetAlivePlayers(map, _players);
            var occupied = false;
            foreach (var (_, pos) in _players)
            {
                if ((pos - boxPos).LengthSquared() <= zone.Radius * zone.Radius)
                {
                    occupied = true;
                    break;
                }
            }

            if (occupied)
                retrieval.Progress += UpdateInterval;

            retrieval.Paused = !occupied;
            if (zone.Occupied != occupied)
            {
                zone.Occupied = occupied;
                Dirty(box, zone);
            }

            var now = _timing.CurTime;
            if (retrieval.Progress >= retrieval.Duration)
            {
                retrieval.Done = true;
                retrieval.Paused = false;
                expedition.Completed = true;
                zone.Active = false;
                Dirty(box, zone);
                _salvage.Announce(map, Loc.GetString("expedition-blackbox-done"));
                continue;
            }

            // Пока качаются данные, сигнал привлекает волны.
            if (now >= retrieval.NextWave)
            {
                retrieval.NextWave = now + retrieval.WaveInterval;
                _waves.ForceWave(map);
            }
        }
    }
}
