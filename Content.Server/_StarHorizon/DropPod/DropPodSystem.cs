using System.Linq;
using System.Numerics;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.DeviceLinking;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Popups;
using Content.Shared.Trigger;
using Content.Shared.Storage.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SalvageSystem _salvage = default!;

    /// <summary>Меньше этого времени до закрытия экспедиции сбрасываться на неё уже поздно.</summary>
    private static readonly TimeSpan PlanetMinTimeLeft = TimeSpan.FromSeconds(90);
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodComponent, StorageOpenAttemptEvent>(OnStorageOpenAttempt);
        SubscribeLocalEvent<DropPodComponent, ComponentShutdown>(OnPodShutdown);
        SubscribeLocalEvent<DropPodComponent, TriggerEvent>(OnTrigger);
        SubscribeLocalEvent<DropPodComponent, FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<DropPodComponent, FTLCompletedEvent>(OnFTLCompleted);

        SubscribeLocalEvent<DropPodConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodLaunchMessage>(OnLaunchMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodAbortMessage>(OnAbortMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodArmMessage>(OnArmMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodArmPlanetMessage>(OnArmPlanetMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodLaunchToPlanetMessage>(OnLaunchToPlanetMessage);
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

    /// <summary>Цель-точка + сигнал: кнопка «Запуск» на консоли.</summary>
    private void OnLaunchMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchMessage args)
    {
        if (ArmPoint(ent, args.Target))
            SendLaunchSignal(ent);
    }

    /// <summary>Только цель-точка: взлёт позже, от триггера любого привязанного устройства.</summary>
    private void OnArmMessage(Entity<DropPodConsoleComponent> ent, ref DropPodArmMessage args)
    {
        ArmPoint(ent, args.Target);
    }

    /// <summary>Только цель-планета: взлёт позже, от триггера любого привязанного устройства.</summary>
    private void OnArmPlanetMessage(Entity<DropPodConsoleComponent> ent, ref DropPodArmPlanetMessage args)
    {
        ArmPlanet(ent, args.Planet);
    }

    /// <summary>Цель-планета + сигнал.</summary>
    private void OnLaunchToPlanetMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchToPlanetMessage args)
    {
        if (ArmPlanet(ent, args.Planet))
            SendLaunchSignal(ent);
    }

    /// <summary>
    /// Записывает привязанным готовым капсулам план сброса на планету. Карта экспедиции при этом не создаётся:
    /// она появится, только когда капсула действительно взлетит (<see cref="FinishCountdown"/>).
    /// </summary>
    private bool ArmPlanet(Entity<DropPodConsoleComponent> console, NetEntity netPlanet)
    {
        var pods = ReadyPods(console, out var error);
        if (pods.Count == 0)
        {
            UpdateConsole(console, error);
            return false;
        }

        if (!TryGetEntity(netPlanet, out var planetUid) || !TryComp(planetUid, out ExpeditionPlanetComponent? planet))
        {
            UpdateConsole(console, "droppod-error-no-planet");
            return false;
        }

        // На идущую экспедицию нужно успеть: капсулам лететь около 20 секунд.
        if (!PlanetAcceptsPods(planet))
        {
            UpdateConsole(console, "droppod-error-planet-closing");
            return false;
        }

        for (var i = 0; i < pods.Count; i++)
        {
            pods[i].Comp.Plan = new DropPodLaunchPlan
            {
                Target = new MapCoordinates(_xform.GetWorldPosition(planetUid.Value), Transform(console).MapID),
                Planet = planetUid,
                Delay = console.Comp.LaunchInterval * i,
            };
        }

        UpdateConsole(console);
        return true;
    }

    /// <summary>Планета принимает капсулы: экспедиции нет (создастся) или она идёт и ещё не скоро закроется.</summary>
    private bool PlanetAcceptsPods(ExpeditionPlanetComponent planet)
    {
        return planet.Status != PlanetExpeditionStatus.Active
               || planet.EndTime - _timing.CurTime >= PlanetMinTimeLeft;
    }

    /// <summary>Цель-шаттл + сигнал.</summary>
    private void OnLaunchToCapturedMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchToCapturedMessage args)
    {
        if (ArmCaptured(ent))
            SendLaunchSignal(ent);
    }

    /// <summary>
    /// Записывает привязанным готовым капсулам план полёта в точку. Капсулы стартуют по очереди; на месте посадки
    /// их разводим в ряд, чтобы не падали друг на друга.
    /// </summary>
    private bool ArmPoint(Entity<DropPodConsoleComponent> console, Vector2 target)
    {
        var pods = ReadyPods(console, out var error);
        if (pods.Count == 0)
        {
            UpdateConsole(console, error);
            return false;
        }

        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) ||
            MathF.Abs(target.X) > console.Comp.MaxCoordinate || MathF.Abs(target.Y) > console.Comp.MaxCoordinate)
        {
            UpdateConsole(console, "droppod-error-bad-target");
            return false;
        }

        var mapId = Transform(console).MapID;
        if (mapId == MapId.Nullspace)
            return false;

        // Цель должна быть в зоне радара консоли (радиус = дальность захвата).
        if ((target - _xform.GetWorldPosition(console)).Length() > console.Comp.CaptureRange)
        {
            UpdateConsole(console, "droppod-error-target-too-far");
            return false;
        }

        for (var i = 0; i < pods.Count; i++)
        {
            var offset = new Vector2((i - (pods.Count - 1) / 2f) * 1.5f, 0f);
            pods[i].Comp.Plan = new DropPodLaunchPlan
            {
                Target = new MapCoordinates(target + offset, mapId),
                Delay = console.Comp.LaunchInterval * i,
            };
        }

        UpdateConsole(console);
        return true;
    }

    /// <summary>Сбрасывает цель у капсул, которые целились в этот шаттл (захват потерян или сброшен).</summary>
    private void DisarmGrid(Entity<DropPodConsoleComponent> console, EntityUid grid)
    {
        foreach (var pod in LinkedPods(console))
        {
            if (pod.Comp.Plan?.Grid == grid)
                pod.Comp.Plan = null;
        }
    }

    /// <summary>Записывает привязанным готовым капсулам план: каждой - свой свободный тайл захваченного шаттла.</summary>
    private bool ArmCaptured(Entity<DropPodConsoleComponent> console)
    {
        var pods = ReadyPods(console, out var error);
        if (pods.Count == 0)
        {
            UpdateConsole(console, error);
            return false;
        }

        if (console.Comp.CapturedGrid is not { } grid || TerminatingOrDeleted(grid))
        {
            console.Comp.CapturedGrid = null;
            UpdateConsole(console, "droppod-error-not-captured");
            return false;
        }

        var taken = new HashSet<Vector2i>();
        for (var i = 0; i < pods.Count; i++)
        {
            if (PickLandingTile(grid, taken) is not { } coords)
            {
                UpdateConsole(console, "droppod-error-bad-target");
                return false;
            }

            pods[i].Comp.Plan = new DropPodLaunchPlan
            {
                Target = _xform.ToMapCoordinates(coords),
                Coords = coords,
                Grid = grid,
                Delay = console.Comp.LaunchInterval * i,
            };
        }

        UpdateConsole(console);
        return true;
    }

    /// <summary>Шлёт сигнал на порт запуска: каждая привязанная капсула получает его как <see cref="TriggerEvent"/>.</summary>
    private void SendLaunchSignal(Entity<DropPodConsoleComponent> console)
    {
        _deviceLink.InvokePort(console.Owner, DropPodConsoleComponent.LaunchPort);
    }

    /// <summary>
    /// Капсула получила триггер (сигнал DeviceLink через <c>TriggerOnSignal</c> или активация через
    /// <c>TriggerOnActivate</c>). Стартует, только если консоль уже записала ей план запуска.
    /// </summary>
    private void OnTrigger(Entity<DropPodComponent> ent, ref TriggerEvent args)
    {
        // TriggerOnActivate срабатывает на любое взаимодействие (например, клик, которым открывают дверь).
        // Для TriggerOnSignal пользователь триггера - отправитель сигнала, то есть устройство с DeviceLinkSource.
        // Запускаем только по сигналу, а обычные клики игнорируем.
        if (args.User is not { } sender || !HasComp<DeviceLinkSourceComponent>(sender))
            return;

        if (ent.Comp.State != DropPodState.Idle || ent.Comp.Used)
            return;

        if (ent.Comp.Plan is not { } plan)
        {
            // Триггер пришёл, а цель на консоли не задана.
            _popup.PopupEntity(Loc.GetString("droppod-fail-no-target"), ent);
            return;
        }

        ent.Comp.Plan = null;
        ent.Comp.TargetGrid = plan.Grid;
        ent.Comp.TargetPlanet = plan.Planet;
        StartCountdown(ent, plan.Target, plan.Coords, plan.Delay);
        args.Handled = true;
    }

    private void OnAbortMessage(Entity<DropPodConsoleComponent> ent, ref DropPodAbortMessage args)
    {
        // «Отмена»: сбрасывает цель у ждущих капсул и останавливает те, что в обратном отсчёте.
        foreach (var pod in LinkedPods(ent))
        {
            pod.Comp.Plan = null;
            if (pod.Comp.State == DropPodState.Countdown)
                Abort(pod);
        }

        UpdateConsole(ent);
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
        if (ent.Comp.CapturedGrid is { } released)
            DisarmGrid(ent, released);

        ent.Comp.CapturingGrid = null;
        ent.Comp.CapturedGrid = null;
        UpdateConsole(ent);
    }

    /// <summary>
    /// Капсулы, привязанные к порту запуска консоли штатной системой DeviceLink. Порядок стабильный
    /// (по номеру сущности), от него зависит очередь запуска.
    /// </summary>
    private List<Entity<DropPodComponent>> LinkedPods(Entity<DropPodConsoleComponent> console)
    {
        var result = new List<Entity<DropPodComponent>>();
        if (!TryComp(console, out DeviceLinkSourceComponent? source))
            return result;

        // Поле закрыто атрибутом Access: читаем его в локальную переменную, дальше работаем с ней.
        var outputs = source.Outputs;
        if (!outputs.TryGetValue(DropPodConsoleComponent.LaunchPort, out var sinks))
            return result;

        foreach (var uid in sinks)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out DropPodComponent? pod))
                continue;

            result.Add((uid, pod));
        }

        result.Sort((a, b) => a.Owner.Id.CompareTo(b.Owner.Id));
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
            pods.Count(p => p.Comp.Plan != null),
            rep?.Comp.State ?? DropPodState.Idle,
            rep?.Comp.StateEnd ?? TimeSpan.Zero,
            rep?.Comp.StateStart ?? TimeSpan.Zero,
            occupants,
            origin,
            ConsoleTarget(pods, rep),
            err,
            grids,
            console.Comp.CapturingGrid == null ? null : GetNetEntity(console.Comp.CapturingGrid.Value),
            console.Comp.CapturedGrid == null ? null : GetNetEntity(console.Comp.CapturedGrid.Value),
            console.Comp.CaptureEnd,
            console.Comp.CaptureTime,
            consoleMap.GetHashCode(),
            console.Comp.CaptureRange,
            _salvage.GetPlanetInfos());

        _ui.SetUiState(console.Owner, DropPodUiKey.Key, state);
    }

    /// <summary>Куда летит (или будет лететь) капсула: цель активной капсулы, иначе задуманная цель.</summary>
    private static Vector2? ConsoleTarget(List<Entity<DropPodComponent>> pods, Entity<DropPodComponent>? rep)
    {
        if (rep != null && rep.Value.Comp.State != DropPodState.Idle)
            return rep.Value.Comp.Target.Position;

        foreach (var pod in pods)
        {
            if (pod.Comp.Plan is { } plan)
                return plan.Target.Position;
        }

        return null;
    }

    private void UpdateConsolesFor(EntityUid pod)
    {
        var query = EntityQueryEnumerator<DropPodConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (LinkedPods((uid, console)).Exists(p => p.Owner == pod))
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
        ent.Comp.Marker = null;

        // На планете карты экспедиции ещё нет (она создаётся при взлёте), поэтому метки до прилёта нет.
        if (ent.Comp.TargetPlanet == null)
            ent.Comp.Marker = Spawn(ent.Comp.MarkerPrototype, coords ?? _xform.ToCoordinates(target));

        // delay - очередь запуска: капсула стартует через i * интервал после предыдущей.
        SetState(ent, DropPodState.Countdown, ent.Comp.CountdownTime + delay);
    }

    private void Abort(Entity<DropPodComponent> ent)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
        ent.Comp.TargetPlanet = null;
        ent.Comp.TargetGrid = null;
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

                    // Прилетели, но карта экспедиции ещё строится: проверяем готовность каждый тик.
                    if (comp.WaitingForExpedition)
                        Arrive(ent);
                    break;

                case DropPodState.Arriving:
                    // Камера на месте посадки: зум возвращается, виньетка (отдельный компонент) снимается.
                    SetZoom(ent, Lerp(comp.PeakZoom, 1f, Smooth(t)));
                    if (now >= comp.StateEnd)
                        BeginFall(ent);
                    break;

                case DropPodState.Landing:
                    // Падение: клубы дыма появляются вдоль траектории по мере того, как капсула пролетает.
                    SpawnTrailPuffs(ent, (float) (now - comp.StateStart).TotalSeconds);
                    if (now >= comp.StateEnd)
                        Land(ent);
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
            DisarmGrid(console, held);
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

                // Захваченный шаттл сразу становится целью привязанных капсул (если они есть).
                if (LinkedPods(console).Count > 0)
                    ArmCaptured(console);
                else
                    UpdateConsole(console);
            }
        }

        // Цель (метка) спадает, если ушла дальше CaptureRange от консоли: шаттл-цель улетел или консоль уехала.
        var dropped = false;
        var consolePos = _xform.GetWorldPosition(console);
        var consoleMap = Transform(console).MapID;
        foreach (var pod in LinkedPods(console))
        {
            if (pod.Comp.Plan is not { } plan)
                continue;

            // Цель-планета лежит за радиусом радара и от расстояния не зависит.
            if (plan.Planet != null)
                continue;

            var inRange = plan.Grid is { } planGrid
                ? !TerminatingOrDeleted(planGrid) && InCaptureRange(console, planGrid)
                : plan.Target.MapId == consoleMap && (plan.Target.Position - consolePos).Length() <= console.Comp.CaptureRange;

            if (inRange)
                continue;

            pod.Comp.Plan = null;
            dropped = true;
        }

        if (dropped)
            UpdateConsole(console, "droppod-error-target-lost");

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
        EntityCoordinates ftlTarget;
        if (ent.Comp.TargetPlanet is { } planetUid)
        {
            // Сброс на планету: экспедиция создаётся именно сейчас, когда капсула взлетает (а не при выборе цели).
            // Если она уже идёт - летим на её карту, ничего не создавая.
            if (!TryComp(planetUid, out ExpeditionPlanetComponent? planet)
                || !PlanetAcceptsPods(planet)
                || !_salvage.StartPlanetExpedition((planetUid, planet))
                || planet.ExpeditionMap is not { } expeditionMap)
            {
                _popup.PopupEntity(Loc.GetString("droppod-error-planet-closing"), ent, PopupType.MediumCaution);
                Abort(ent);
                return;
            }

            ftlTarget = new EntityCoordinates(expeditionMap, Vector2.Zero);
        }
        else
        {
            var target = ent.Comp.Target;
            if (ent.Comp.Marker is { } marker && !TerminatingOrDeleted(marker))
                target = _xform.GetMapCoordinates(marker);

            var mapUid = _map.GetMap(target.MapId);
            if (mapUid == EntityUid.Invalid)
            {
                Abort(ent);
                return;
            }

            ftlTarget = new EntityCoordinates(mapUid, target.Position);
        }

        _shuttle.FTLToCoordinates(
            ent.Owner,
            null,
            ftlTarget,
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
        if (ent.Comp.TargetPlanet is { } planetUid)
        {
            // Сброс на планету: ждём, пока построится карта экспедиции, и садимся на её точку посадки.
            if (!TryGetExpeditionLanding(ent, planetUid, out coords))
                return;
        }
        else if (ent.Comp.TargetGrid is { } grid && !TerminatingOrDeleted(grid) && HasComp<MapGridComponent>(grid))
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

    /// <summary>
    /// Точка посадки на карте экспедиции планеты. Если карта ещё строится (job работает несколько секунд),
    /// возвращает false: капсула остаётся в БСС и повторит попытку, но не дольше <see cref="DropPodComponent.ExpeditionWaitTime"/>.
    /// </summary>
    private bool TryGetExpeditionLanding(Entity<DropPodComponent> ent, EntityUid planetUid, out EntityCoordinates coords)
    {
        coords = default;

        if (!TryComp(planetUid, out ExpeditionPlanetComponent? planet)
            || planet.ExpeditionMap is not { } map
            || TerminatingOrDeleted(map))
        {
            // Экспедиция закрылась по пути: садимся там, куда капсулу привёз FTL.
            ent.Comp.WaitingForExpedition = false;
            coords = Transform(ent).Coordinates;
            return true;
        }

        if (planet.Status != PlanetExpeditionStatus.Active || !TryComp(map, out SalvageExpeditionComponent? expedition))
        {
            if (!ent.Comp.WaitingForExpedition)
            {
                ent.Comp.WaitingForExpedition = true;
                ent.Comp.ExpeditionWaitEnd = _timing.CurTime + ent.Comp.ExpeditionWaitTime;
            }

            if (_timing.CurTime < ent.Comp.ExpeditionWaitEnd)
                return false;

            // Карта так и не построилась: садимся в начале координат.
            ent.Comp.WaitingForExpedition = false;
            coords = new EntityCoordinates(map, Vector2.Zero);
            return true;
        }

        ent.Comp.WaitingForExpedition = false;

        // Карта экспедиции создаётся на паузе; с прилётом капсулы она оживает для всех.
        _map.SetPaused(map, false);

        // Каждой капсуле своя точка рядом с площадкой посадки, чтобы не садились друг на друга.
        coords = new EntityCoordinates(map, expedition.LandingCoordinates + _random.NextVector2(0.5f, 3f));
        return true;
    }

    /// <summary>
    /// Виньетка снята: начинается падение. Настоящая капсула остаётся скрытой, вместо неё летит анимированная
    /// сущность (диагональный спрайт Goobstation), а за ней по траектории остаётся шлейф и клубы дыма.
    /// </summary>
    private void BeginFall(Entity<DropPodComponent> ent)
    {
        ClearVignette(ent);
        ent.Comp.PuffsSpawned = 0;

        var coords = Transform(ent).Coordinates;
        SpawnAtPosition(ent.Comp.LandingAnimationEffect, coords);
        SpawnAtPosition(ent.Comp.TrailEffect, coords);
        _audio.PlayPvs(ent.Comp.LandingAnimationSound, ent);

        SetState(ent, DropPodState.Landing, ent.Comp.FallTime);
    }

    /// <summary>Клубы дыма вдоль траектории: каждый появляется там, где в этот момент пролетает капсула.</summary>
    private void SpawnTrailPuffs(Entity<DropPodComponent> ent, float elapsed)
    {
        var count = ent.Comp.TrailPuffCount;
        if (count <= 0)
            return;

        var origin = _xform.GetMapCoordinates(ent.Owner);
        while (ent.Comp.PuffsSpawned < count)
        {
            var frame = DropPodLandingGeometry.PuffFrame(ent.Comp.PuffsSpawned, count);
            if (elapsed < DropPodLandingGeometry.FrameStart(frame))
                return;

            var pos = origin.Position + DropPodLandingGeometry.FrameOffset(frame);
            SpawnAtPosition(ent.Comp.SmokePuffEffect, _xform.ToCoordinates(new MapCoordinates(pos, origin.MapId)));
            ent.Comp.PuffsSpawned++;
        }
    }

    /// <summary>Падение закончено: капсула появляется, бьёт по земле, поднимает дым и становится прикрученной.</summary>
    private void Land(Entity<DropPodComponent> ent)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
        ent.Comp.TargetGrid = null;
        ent.Comp.TargetPlanet = null;

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

        var coords = Transform(ent).Coordinates;
        SpawnAtPosition(ent.Comp.LandingEffect, coords);
        _audio.PlayPvs(ent.Comp.LandingSound, ent);

        // Удар: облако дыма вокруг капсулы.
        var impact = _xform.GetMapCoordinates(ent.Owner);
        for (var i = 0; i < ent.Comp.ImpactPuffCount; i++)
        {
            var offset = _random.NextVector2(0.2f, 1.1f);
            SpawnAtPosition(ent.Comp.SmokePuffEffect, _xform.ToCoordinates(new MapCoordinates(impact.Position + offset, impact.MapId)));
        }

        if (!ent.Comp.LandingDamage.Empty)
        {
            foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(impact, ent.Comp.LandingRadius))
            {
                if (mob.Owner == ent.Owner)
                    continue;

                _damageable.TryChangeDamage(mob.Owner, ent.Comp.LandingDamage, origin: ent.Owner);
            }
        }

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
