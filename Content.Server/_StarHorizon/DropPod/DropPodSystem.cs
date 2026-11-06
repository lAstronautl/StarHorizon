using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tools.Systems;
using Content.Shared.Storage.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Tools;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.DropPod;

/// <summary>
/// Логика дроп-подов и консоли управления ими.
/// Стадии: <see cref="DropPodState.Countdown"/> -> <see cref="DropPodState.ZoomingOut"/> (камера отдаляется) ->
/// <see cref="DropPodState.InBluespace"/> (капсулы нет на карте) -> <see cref="DropPodState.Landing"/> (удар и возврат камеры).
/// </summary>
public sealed class DropPodSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    private static readonly ProtoId<ToolQualityPrototype> LinkQuality = "Pulsing";
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodComponent, StorageOpenAttemptEvent>(OnStorageOpenAttempt);
        SubscribeLocalEvent<DropPodComponent, ComponentShutdown>(OnPodShutdown);
        SubscribeLocalEvent<DropPodComponent, InteractUsingEvent>(OnPodInteractUsing,
            before: new[] { typeof(SharedEntityStorageSystem) });
        SubscribeLocalEvent<DropPodConsoleComponent, InteractUsingEvent>(OnConsoleInteractUsing,
            before: new[] { typeof(SharedEntityStorageSystem) });
        SubscribeLocalEvent<DropPodComponent, FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<DropPodComponent, FTLCompletedEvent>(OnFTLCompleted);

        SubscribeLocalEvent<DropPodConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodLaunchMessage>(OnLaunchMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodAbortMessage>(OnAbortMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodLaunchToCapturedMessage>(OnLaunchToCapturedMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodCaptureMessage>(OnCaptureMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodReleaseMessage>(OnReleaseMessage);
    }

    #region Pod events

    private void OnStorageOpenAttempt(Entity<DropPodComponent> ent, ref StorageOpenAttemptEvent args)
    {
        // Во время полёта дверь заблокирована.
        if (ent.Comp.State is not (DropPodState.Idle or DropPodState.Countdown))
            args.Cancelled = true;
    }

    private void OnPodShutdown(Entity<DropPodComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
    }

    #endregion

    #region Console

    private void OnConsoleOpened(Entity<DropPodConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateConsole(ent);
    }

    private void OnLaunchMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchMessage args)
    {
        var pods = ReadyPods(ent, out var error);
        if (pods.Count == 0)
        {
            UpdateConsole(ent, error);
            return;
        }

        var target = args.Target;
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) ||
            MathF.Abs(target.X) > ent.Comp.MaxCoordinate || MathF.Abs(target.Y) > ent.Comp.MaxCoordinate)
        {
            UpdateConsole(ent, "droppod-error-bad-target");
            return;
        }

        var mapId = Transform(ent).MapID;
        if (mapId == MapId.Nullspace)
            return;

        // Капсулы стартуют по очереди; на месте посадки их разводим в ряд, чтобы не падали друг на друга.
        for (var i = 0; i < pods.Count; i++)
        {
            var offset = new Vector2((i - (pods.Count - 1) / 2f) * 1.5f, 0f);
            StartCountdown(pods[i], new MapCoordinates(target + offset, mapId), null, ent.Comp.LaunchInterval * i);
        }
    }

    private void OnAbortMessage(Entity<DropPodConsoleComponent> ent, ref DropPodAbortMessage args)
    {
        foreach (var pod in LinkedPods(ent))
        {
            if (pod.Comp.State == DropPodState.Countdown)
                Abort(pod);
        }
    }

    private void OnLaunchToCapturedMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchToCapturedMessage args)
    {
        var pods = ReadyPods(ent, out var error);
        if (pods.Count == 0)
        {
            UpdateConsole(ent, error);
            return;
        }

        if (ent.Comp.CapturedGrid is not { } grid || TerminatingOrDeleted(grid))
        {
            ent.Comp.CapturedGrid = null;
            UpdateConsole(ent, "droppod-error-not-captured");
            return;
        }

        // Каждой капсуле - свой свободный тайл, по очереди с интервалом.
        var taken = new HashSet<Vector2i>();
        for (var i = 0; i < pods.Count; i++)
        {
            if (PickLandingTile(grid, taken) is not { } coords)
            {
                UpdateConsole(ent, "droppod-error-bad-target");
                return;
            }

            pods[i].Comp.TargetGrid = grid;
            StartCountdown(pods[i], _xform.ToMapCoordinates(coords), coords, ent.Comp.LaunchInterval * i);
        }
    }

    private void OnCaptureMessage(Entity<DropPodConsoleComponent> ent, ref DropPodCaptureMessage args)
    {
        if (!TryGetEntity(args.Grid, out var grid) || !HasComp<ShuttleComponent>(grid) || TerminatingOrDeleted(grid))
        {
            UpdateConsole(ent, "droppod-error-bad-grid");
            return;
        }

        if (!InCaptureRange(ent, grid.Value))
        {
            UpdateConsole(ent, "droppod-error-out-of-range");
            return;
        }

        ent.Comp.CapturedGrid = null;
        ent.Comp.CapturingGrid = grid;
        ent.Comp.CaptureEnd = _timing.CurTime + ent.Comp.CaptureTime;
        UpdateConsole(ent);
    }

    private void OnReleaseMessage(Entity<DropPodConsoleComponent> ent, ref DropPodReleaseMessage args)
    {
        ent.Comp.CapturingGrid = null;
        ent.Comp.CapturedGrid = null;
        UpdateConsole(ent);
    }

    /// <summary>Капсулы, привязанные к консоли мультитулом (мёртвые отвязываются).</summary>
    private List<Entity<DropPodComponent>> LinkedPods(Entity<DropPodConsoleComponent> console)
    {
        var result = new List<Entity<DropPodComponent>>();
        console.Comp.LinkedPods.RemoveAll(p => TerminatingOrDeleted(p) || !HasComp<DropPodComponent>(p));

        foreach (var uid in console.Comp.LinkedPods)
        {
            result.Add((uid, Comp<DropPodComponent>(uid)));
        }

        return result;
    }

    /// <summary>Капсулы, которые можно запускать прямо сейчас: простаивают и ещё не использованы.</summary>
    private List<Entity<DropPodComponent>> ReadyPods(Entity<DropPodConsoleComponent> console, out string error)
    {
        var all = LinkedPods(console);
        var ready = all.FindAll(p => p.Comp.State == DropPodState.Idle && !p.Comp.Used);

        error = all.Count == 0 ? "droppod-error-no-pod"
            : all.TrueForAll(p => p.Comp.Used) ? "droppod-error-used"
            : "droppod-error-busy";
        return ready;
    }

    /// <summary>Центр грида в мировых координатах.</summary>
    private Vector2 GridCenter(EntityUid grid)
    {
        if (!TryComp(grid, out MapGridComponent? comp))
            return _xform.GetWorldPosition(grid);

        return Vector2.Transform(comp.LocalAABB.Center, _xform.GetWorldMatrix(grid));
    }

    private float GridRadius(EntityUid grid)
    {
        return TryComp(grid, out MapGridComponent? comp)
            ? MathF.Max(comp.LocalAABB.Width, comp.LocalAABB.Height) / 2f
            : 1f;
    }

    /// <summary>Шаттл в пределах дальности захвата от консоли (и на той же карте).</summary>
    private bool InCaptureRange(Entity<DropPodConsoleComponent> console, EntityUid grid)
    {
        if (Transform(console).MapID != Transform(grid).MapID)
            return false;

        var distance = (GridCenter(grid) - _xform.GetWorldPosition(console)).Length();
        return distance <= console.Comp.CaptureRange;
    }

    #region Multitool linking

    private void OnPodInteractUsing(EntityUid uid, DropPodComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, LinkQuality))
            return;

        args.Handled = true;

        var buffer = EnsureComp<DropPodLinkBufferComponent>(args.Used);
        if (buffer.Console is { } console && !TerminatingOrDeleted(console) &&
            TryComp(console, out DropPodConsoleComponent? consoleComp))
        {
            Link((console, consoleComp), uid, args.User);
            buffer.Console = null;
            return;
        }

        buffer.Pod = uid;
        buffer.Console = null;
        _popup.PopupEntity(Loc.GetString("droppod-link-pod-saved"), uid, args.User);
    }

    private void OnConsoleInteractUsing(EntityUid uid, DropPodConsoleComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, LinkQuality))
            return;

        args.Handled = true;

        var buffer = EnsureComp<DropPodLinkBufferComponent>(args.Used);
        if (buffer.Pod is { } pod && !TerminatingOrDeleted(pod) && HasComp<DropPodComponent>(pod))
        {
            Link((uid, comp), pod, args.User);
            buffer.Pod = null;
            return;
        }

        buffer.Console = uid;
        buffer.Pod = null;
        _popup.PopupEntity(Loc.GetString("droppod-link-console-saved"), uid, args.User);
    }

    /// <summary>Привязывает капсулу к консоли; если уже привязана - отвязывает.</summary>
    private void Link(Entity<DropPodConsoleComponent> console, EntityUid pod, EntityUid user)
    {
        if (console.Comp.LinkedPods.Remove(pod))
        {
            _popup.PopupEntity(Loc.GetString("droppod-link-removed"), console, user);
        }
        else
        {
            console.Comp.LinkedPods.Add(pod);
            _popup.PopupEntity(Loc.GetString("droppod-link-done", ("count", console.Comp.LinkedPods.Count)), console, user);
        }

        UpdateConsole(console);
    }

    #endregion

    private void UpdateConsole(Entity<DropPodConsoleComponent> console, string? error = null)
    {
        var pods = LinkedPods(console);
        var origin = _xform.GetWorldPosition(console);
        var err = error == null ? null : Loc.GetString(error);

        var grids = new List<DropPodGridInfo>();
        var consoleMap = Transform(console).MapID;
        var gridQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent, MetaDataComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform, out var meta))
        {
            if (grids.Count >= 64)
                break;

            // На карте в окне показывается только карта консоли.
            if (gridXform.MapID != consoleMap)
                continue;

            grids.Add(new DropPodGridInfo(GetNetEntity(gridUid), meta.EntityName, GridCenter(gridUid), GridRadius(gridUid)));
        }

        // Показываем самую «продвинутую» капсулу: активную, иначе первую готовую, иначе первую.
        Entity<DropPodComponent>? rep = null;
        foreach (var p in pods)
        {
            if (p.Comp.State != DropPodState.Idle)
            {
                rep = p;
                break;
            }
        }

        if (rep == null)
        {
            foreach (var p in pods)
            {
                if (!p.Comp.Used)
                {
                    rep = p;
                    break;
                }
            }
        }

        if (rep == null && pods.Count > 0)
            rep = pods[0];

        var occupants = 0;
        foreach (var pod in pods)
        {
            if (TryComp(pod, out EntityStorageComponent? storage))
                occupants += storage.Contents.ContainedEntities.Count;
        }

        var state = new DropPodConsoleState(
            pods.Count > 0,
            pods.Count,
            pods.Count(p => !p.Comp.Used),
            rep?.Comp.State ?? DropPodState.Idle,
            rep?.Comp.StateEnd ?? TimeSpan.Zero,
            rep?.Comp.StateStart ?? TimeSpan.Zero,
            occupants,
            origin,
            rep != null && rep.Value.Comp.State != DropPodState.Idle ? rep.Value.Comp.Target.Position : null,
            err,
            grids,
            console.Comp.CapturingGrid == null ? null : GetNetEntity(console.Comp.CapturingGrid.Value),
            console.Comp.CapturedGrid == null ? null : GetNetEntity(console.Comp.CapturedGrid.Value),
            console.Comp.CaptureEnd,
            console.Comp.CaptureTime,
            consoleMap.GetHashCode(),
            console.Comp.CaptureRange);

        _ui.SetUiState(console.Owner, DropPodUiKey.Key, state);
    }

    private void UpdateConsolesFor(EntityUid pod)
    {
        var query = EntityQueryEnumerator<DropPodConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (console.LinkedPods.Contains(pod))
                UpdateConsole((uid, console));
        }
    }

    #endregion

    #region State machine

    private void SetState(Entity<DropPodComponent> ent, DropPodState state, TimeSpan duration)
    {
        ent.Comp.State = state;
        ent.Comp.StateStart = _timing.CurTime;
        ent.Comp.StateEnd = _timing.CurTime + duration;
        _appearance.SetData(ent, DropPodVisuals.State, state);
        UpdateConsolesFor(ent);
    }

    private void StartCountdown(Entity<DropPodComponent> ent, MapCoordinates target, EntityCoordinates? coords = null, TimeSpan delay = default)
    {
        ent.Comp.Target = target;
        if (coords == null)
            ent.Comp.TargetGrid = null;

        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = Spawn(ent.Comp.MarkerPrototype, coords ?? _xform.ToCoordinates(target));

        // delay - очередь запуска: капсула стартует через i * интервал после предыдущей.
        SetState(ent, DropPodState.Countdown, ent.Comp.CountdownTime + delay);
    }

    private void Abort(Entity<DropPodComponent> ent)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
        SetState(ent, DropPodState.Idle, TimeSpan.Zero);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var consoles = EntityQueryEnumerator<DropPodConsoleComponent>();
        while (consoles.MoveNext(out var consoleUid, out var console))
        {
            UpdateCapture((consoleUid, console), now);
        }

        CleanupVignettes();

        var query = EntityQueryEnumerator<DropPodComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.State == DropPodState.Idle)
                continue;

            var ent = (uid, comp);
            var duration = (comp.StateEnd - comp.StateStart).TotalSeconds;
            var t = duration <= 0 ? 1f : (float) Math.Clamp((now - comp.StateStart).TotalSeconds / duration, 0, 1);

            switch (comp.State)
            {
                case DropPodState.Countdown:
                    if (now >= comp.StateEnd)
                        FinishCountdown(ent);
                    break;

                case DropPodState.ZoomingOut:
                    // Это стартовая фаза настоящего FTL; в БСС капсулу переводит FTLStartedEvent.
                    SetZoom(ent, Lerp(1f, comp.PeakZoom, EaseOut(t)));
                    break;

                case DropPodState.InBluespace:
                    // Выход из БСС приходит событием FTLCompletedEvent.
                    SetZoom(ent, comp.PeakZoom);
                    break;

                case DropPodState.Arriving:
                    // Камера на месте посадки: зум возвращается, виньетка (отдельный компонент) снимается.
                    SetZoom(ent, Lerp(comp.PeakZoom, 1f, Smooth(t)));
                    if (now >= comp.StateEnd)
                        Land(ent);
                    break;

                case DropPodState.Landing:
                    if (now >= comp.StateEnd)
                        FinishLanding(ent);
                    break;
            }
        }
    }

    /// <summary>Снимает затемнение с тех, кто больше не в капсуле, или если капсула уничтожена / не летит.</summary>
    private void CleanupVignettes()
    {
        var fades = EntityQueryEnumerator<DropPodVignetteComponent>();
        while (fades.MoveNext(out var uid, out var fade))
        {
            var valid = TryComp(fade.Pod, out DropPodComponent? pod) &&
                        !TerminatingOrDeleted(fade.Pod) &&
                        pod.State is DropPodState.ZoomingOut or DropPodState.InBluespace or DropPodState.Arriving &&
                        TryComp(fade.Pod, out EntityStorageComponent? storage) &&
                        storage.Contents.Contains(uid);

            if (!valid)
                RemCompDeferred<DropPodVignetteComponent>(uid);
        }
    }

    private void UpdateCapture(Entity<DropPodConsoleComponent> console, TimeSpan now)
    {
        if (console.Comp.CapturedGrid is { } captured && TerminatingOrDeleted(captured))
        {
            console.Comp.CapturedGrid = null;
            UpdateConsole(console);
        }

        // Захват спадает, если шаттл ушёл дальше CaptureRange. Уже запущенные капсулы летят к своей цели как летели.
        if (console.Comp.CapturedGrid is { } held && !InCaptureRange(console, held))
        {
            console.Comp.CapturedGrid = null;
            UpdateConsole(console, "droppod-error-capture-lost");
        }

        if (console.Comp.CapturingGrid is { } grid)
        {
            if (TerminatingOrDeleted(grid))
            {
                console.Comp.CapturingGrid = null;
                UpdateConsole(console, "droppod-error-bad-grid");
            }
            else if (!InCaptureRange(console, grid))
            {
                console.Comp.CapturingGrid = null;
                UpdateConsole(console, "droppod-error-capture-lost");
            }
            else if (now >= console.Comp.CaptureEnd)
            {
                console.Comp.CapturedGrid = grid;
                console.Comp.CapturingGrid = null;
                UpdateConsole(console);
            }
        }

        // Пока окно открыто, шлём свежие позиции шаттлов на карту.
        if (now >= console.Comp.NextUiUpdate && _ui.IsUiOpen(console.Owner, DropPodUiKey.Key))
        {
            console.Comp.NextUiUpdate = now + TimeSpan.FromSeconds(1);
            UpdateConsole(console);
        }
    }

    private void FinishCountdown(Entity<DropPodComponent> ent)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage) || storage.Open)
        {
            _popup.PopupEntity(Loc.GetString("droppod-fail-open"), ent, PopupType.MediumCaution);
            Abort(ent);
            return;
        }

        // Остаток прошлого FTL (кулдаун) мешает запуску нового.
        RemComp<FTLComponent>(ent);

        // Настоящий БСС, как у команды ftlentity: старт, FTL-карта, фаза прибытия, телепорт.
        // Летим на координаты метки; точное место (в т.ч. тайл захваченного шаттла) определяется при посадке.
        var target = ent.Comp.Target;
        if (ent.Comp.Marker is { } marker && !TerminatingOrDeleted(marker))
            target = _xform.GetMapCoordinates(marker);

        var mapUid = _map.GetMap(target.MapId);
        if (mapUid == EntityUid.Invalid)
        {
            Abort(ent);
            return;
        }

        _shuttle.FTLToCoordinates(
            ent.Owner,
            null,
            new EntityCoordinates(mapUid, target.Position),
            Angle.Zero,
            (float) ent.Comp.ZoomOutTime.TotalSeconds,
            (float) ent.Comp.FlightTime.TotalSeconds + _shuttle.DefaultArrivalTime);

        if (!HasComp<FTLComponent>(ent))
        {
            Abort(ent);
            return;
        }

        SetState(ent, DropPodState.ZoomingOut, ent.Comp.ZoomOutTime);
        // Камера отдаляется сразу, а виньетка начинает расти с задержкой и успевает дорасти к концу взлёта.
        var delay = TimeSpan.FromTicks(Math.Min(ent.Comp.VignetteDelay.Ticks, ent.Comp.ZoomOutTime.Ticks / 2));
        SetVignette(ent, 0f, 1f, ent.Comp.ZoomOutTime - delay, delay);
    }

    /// <summary>Стартовая фаза FTL закончилась: капсула на FTL-карте, у пассажиров виньетка и отдалённая камера.</summary>
    private void OnFTLStarted(Entity<DropPodComponent> ent, ref FTLStartedEvent args)
    {
        if (ent.Comp.State != DropPodState.ZoomingOut)
            return;

        SpawnAtPosition(ent.Comp.LaunchEffect, new EntityCoordinates(args.FromMapUid ?? EntityUid.Invalid, _xform.GetWorldPosition(ent.Owner)));

        // В БСС капсула остаётся видимой, как шаттл в FTL: на FTL-карте нет освещения,
        // поэтому подсвечиваем её собственным светом, а на корпусе горит синий индикатор.
        _light.SetEnabled(ent, true);
        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, false, body: physics);

        var total = TryComp(ent, out FTLComponent? ftl) ? ftl.TravelTime : (float) ent.Comp.FlightTime.TotalSeconds;
        SetState(ent, DropPodState.InBluespace, TimeSpan.FromSeconds(total));
    }

    /// <summary>БСС закончился (FTL доставил капсулу): камера переезжает на место посадки.</summary>
    private void OnFTLCompleted(Entity<DropPodComponent> ent, ref FTLCompletedEvent args)
    {
        if (ent.Comp.State != DropPodState.InBluespace)
            return;

        Arrive(ent);
    }

    /// <summary>
    /// Камера (вместе со скрытой капсулой) переносится на место посадки, виньетка плавно снимается.
    /// Игрок видит, куда падает; физически капсула приземлится, когда виньетка снята (<see cref="Land"/>).
    /// </summary>
    private void Arrive(Entity<DropPodComponent> ent)
    {
        EntityCoordinates coords;
        if (ent.Comp.TargetGrid is { } grid && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid))
        {
            // Шаттл мог улететь куда угодно - приземляемся на него. Метка едет вместе с гридом,
            // но если под ней успели что-то построить или поставить, выбираем новый свободный тайл.
            var markerCoords = ent.Comp.Marker is { } m && !TerminatingOrDeleted(m)
                ? (EntityCoordinates?) Transform(m).Coordinates
                : null;

            if (markerCoords is { } mc && mc.EntityId == grid && IsTileFree(grid, mc))
                coords = mc;
            else
                coords = PickLandingTile(grid) ?? new EntityCoordinates(grid, Vector2.Zero);
        }
        else
        {
            // Метка могла уехать вместе с гридом - приземляемся туда, где она сейчас.
            var landingPos = ent.Comp.Target;
            if (ent.Comp.Marker is { } marker && !TerminatingOrDeleted(marker))
                landingPos = _xform.GetMapCoordinates(marker);

            coords = _xform.ToCoordinates(landingPos);
        }

        // Капсула ещё не приземлилась: скрыта и без коллизий, метка остаётся на месте.
        _xform.SetCoordinates(ent.Owner, coords);
        _xform.AttachToGridOrMap(ent.Owner);

        _appearance.SetData(ent, DropPodVisuals.InFlight, true);
        _light.SetEnabled(ent, false);
        if (TryComp(ent, out PhysicsComponent? hidden))
            _physics.SetCanCollide(ent, false, body: hidden);

        SetVignette(ent, 1f, 0f, ent.Comp.ArrivalViewTime);
        SetState(ent, DropPodState.Arriving, ent.Comp.ArrivalViewTime);
    }

    /// <summary>Виньетка снята: капсула физически приземляется с анимацией падения.</summary>
    private void Land(Entity<DropPodComponent> ent)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
        ent.Comp.TargetGrid = null;

        ClearVignette(ent);

        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, true, body: physics);
        _appearance.SetData(ent, DropPodVisuals.InFlight, false);

        // Одноразовая: больше не запускается. В прототипе капсула прикручена, при взлёте движок её
        // откручивает (перемещение снимает крепление), а после посадки прикручиваем обратно.
        ent.Comp.Used = true;
        if (!_xform.AnchorEntity(ent.Owner) && TryComp(ent, out PhysicsComponent? body))
        {
            // Посадка не на грид (в космос): прикрутить не к чему, капсула остаётся неподвижной.
            _physics.SetBodyType(ent, BodyType.Static, body: body);
        }

        SpawnAtPosition(ent.Comp.LandingEffect, Transform(ent).Coordinates);
        SpawnAtPosition(ent.Comp.TrailEffect, Transform(ent).Coordinates);
        SpawnAtPosition(ent.Comp.LaunchEffect, Transform(ent).Coordinates);
        _audio.PlayPvs(ent.Comp.LandingSound, ent);

        if (!ent.Comp.LandingDamage.Empty)
        {
            var landing = _xform.GetMapCoordinates(ent.Owner);
            foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(landing, ent.Comp.LandingRadius))
            {
                if (mob.Owner == ent.Owner)
                    continue;

                _damageable.TryChangeDamage(mob.Owner, ent.Comp.LandingDamage, origin: ent.Owner);
            }
        }

        SetState(ent, DropPodState.Landing, ent.Comp.LandingTime);
    }

    private void FinishLanding(Entity<DropPodComponent> ent)
    {
        ResetZoom(ent);
        RemCompDeferred<FTLComponent>(ent);
        SetState(ent, DropPodState.Idle, TimeSpan.Zero);
    }

    #endregion

    #region Helpers

    /// <summary>Задаёт отдаление камеры всем, кто сидит внутри. Клиент сам плавно интерполирует.</summary>
    private void SetZoom(Entity<DropPodComponent> ent, float zoom)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        var vec = new Vector2(zoom, zoom);
        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            _eye.SetZoom(occupant, vec, ignoreLimits: true);
        }
    }

    /// <summary>
    /// Случайный тайл грида без закреплённых твёрдых структур (стен, машин, столов...).
    /// Если таких нет - любой тайл, если и тайлов нет - начало координат грида.
    /// </summary>
    private EntityCoordinates? PickLandingTile(EntityUid gridUid, HashSet<Vector2i>? taken = null)
    {
        if (!TryComp(gridUid, out MapGridComponent? grid))
            return null;

        var free = new List<Vector2i>();
        var any = new List<Vector2i>();
        foreach (var tile in _map.GetAllTiles(gridUid, grid))
        {
            if (taken != null && taken.Contains(tile.GridIndices))
                continue;

            any.Add(tile.GridIndices);
            if (IsTileFree((gridUid, grid), tile.GridIndices))
                free.Add(tile.GridIndices);
        }

        var pool = free.Count > 0 ? free : any;
        if (pool.Count == 0)
            return new EntityCoordinates(gridUid, Vector2.Zero);

        var picked = _random.Pick(pool);
        taken?.Add(picked);
        return _map.GridTileToLocal(gridUid, grid, picked);
    }

    private bool IsTileFree(EntityUid gridUid, EntityCoordinates coords)
    {
        if (!TryComp(gridUid, out MapGridComponent? grid))
            return false;

        var indices = _map.TileIndicesFor(gridUid, grid, coords);
        return !_map.GetTileRef(gridUid, grid, indices).Tile.IsEmpty && IsTileFree((gridUid, grid), indices);
    }

    private bool IsTileFree(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var anchored in _map.GetAnchoredEntities(grid, indices))
        {
            if (TryComp(anchored, out PhysicsComponent? body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }

    private void ResetZoom(Entity<DropPodComponent> ent)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            _eye.ResetZoom(occupant);
        }
    }

    /// <summary>Затемняет экран всем пассажирам: альфа от <paramref name="from"/> к <paramref name="to"/> за <paramref name="duration"/>.</summary>
    private void SetVignette(Entity<DropPodComponent> ent, float from, float to, TimeSpan duration, TimeSpan delay = default)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            var fade = EnsureComp<DropPodVignetteComponent>(occupant);
            fade.Pod = ent;
            fade.From = from;
            fade.To = to;
            fade.Start = _timing.CurTime + delay;
            fade.End = fade.Start + duration;
            Dirty(occupant, fade);
        }
    }

    private void ClearVignette(Entity<DropPodComponent> ent)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            RemCompDeferred<DropPodVignetteComponent>(occupant);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Быстрый старт и плавное замедление: отдаление начинается сразу.</summary>
    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    #endregion
}
