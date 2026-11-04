using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodComponent, StorageOpenAttemptEvent>(OnStorageOpenAttempt);
        SubscribeLocalEvent<DropPodComponent, ComponentShutdown>(OnPodShutdown);

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
        var pod = FindPod(ent);
        if (pod == null || pod.Value.Comp.State != DropPodState.Idle)
        {
            UpdateConsole(ent, "droppod-error-no-pod");
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

        StartCountdown(pod.Value, new MapCoordinates(target, mapId));
    }

    private void OnAbortMessage(Entity<DropPodConsoleComponent> ent, ref DropPodAbortMessage args)
    {
        var pod = FindPod(ent);
        if (pod == null || pod.Value.Comp.State != DropPodState.Countdown)
            return;

        Abort(pod.Value);
    }

    private void OnLaunchToCapturedMessage(Entity<DropPodConsoleComponent> ent, ref DropPodLaunchToCapturedMessage args)
    {
        var pod = FindPod(ent);
        if (pod == null || pod.Value.Comp.State != DropPodState.Idle)
        {
            UpdateConsole(ent, "droppod-error-no-pod");
            return;
        }

        if (ent.Comp.CapturedGrid is not { } grid || TerminatingOrDeleted(grid))
        {
            ent.Comp.CapturedGrid = null;
            UpdateConsole(ent, "droppod-error-not-captured");
            return;
        }

        if (PickLandingTile(grid) is not { } coords)
        {
            UpdateConsole(ent, "droppod-error-bad-target");
            return;
        }

        pod.Value.Comp.TargetGrid = grid;
        StartCountdown(pod.Value, _xform.ToMapCoordinates(coords), coords);
    }

    private void OnCaptureMessage(Entity<DropPodConsoleComponent> ent, ref DropPodCaptureMessage args)
    {
        if (!TryGetEntity(args.Grid, out var grid) || !HasComp<ShuttleComponent>(grid) || TerminatingOrDeleted(grid))
        {
            UpdateConsole(ent, "droppod-error-bad-grid");
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

    private Entity<DropPodComponent>? FindPod(Entity<DropPodConsoleComponent> console)
    {
        // Капсула в процессе запуска остаётся привязанной, даже если улетела далеко.
        if (console.Comp.LinkedPod is { } linked &&
            TryComp(linked, out DropPodComponent? linkedComp) &&
            !TerminatingOrDeleted(linked))
        {
            if (linkedComp.State != DropPodState.Idle)
                return (linked, linkedComp);

            var consolePos = _xform.GetMapCoordinates(console);
            var podPos = _xform.GetMapCoordinates(linked);
            if (consolePos.MapId == podPos.MapId &&
                (consolePos.Position - podPos.Position).Length() <= console.Comp.LinkRange)
                return (linked, linkedComp);
        }

        console.Comp.LinkedPod = null;

        Entity<DropPodComponent>? best = null;
        var bestDist = float.MaxValue;
        var origin = _xform.GetMapCoordinates(console);

        foreach (var pod in _lookup.GetEntitiesInRange<DropPodComponent>(origin, console.Comp.LinkRange))
        {
            var dist = (_xform.GetWorldPosition(pod) - origin.Position).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = pod;
        }

        console.Comp.LinkedPod = best?.Owner;
        return best;
    }

    private void UpdateConsole(Entity<DropPodConsoleComponent> console, string? error = null)
    {
        var pod = FindPod(console);
        var origin = _xform.GetWorldPosition(console);
        var err = error == null ? null : Loc.GetString(error);

        var grids = new List<DropPodGridInfo>();
        var gridQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent, MetaDataComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform, out var meta))
        {
            if (grids.Count >= 64)
                break;

            grids.Add(new DropPodGridInfo(GetNetEntity(gridUid), meta.EntityName, _xform.GetWorldPosition(gridXform)));
        }

        var state = new DropPodConsoleState(
            pod != null,
            pod?.Comp.State ?? DropPodState.Idle,
            pod?.Comp.StateEnd ?? TimeSpan.Zero,
            pod?.Comp.StateStart ?? TimeSpan.Zero,
            pod != null && TryComp(pod.Value, out EntityStorageComponent? storage) ? storage.Contents.ContainedEntities.Count : 0,
            origin,
            pod != null && pod.Value.Comp.State != DropPodState.Idle ? pod.Value.Comp.Target.Position : null,
            err,
            grids,
            console.Comp.CapturingGrid == null ? null : GetNetEntity(console.Comp.CapturingGrid.Value),
            console.Comp.CapturedGrid == null ? null : GetNetEntity(console.Comp.CapturedGrid.Value),
            console.Comp.CaptureEnd,
            console.Comp.CaptureTime);

        _ui.SetUiState(console.Owner, DropPodUiKey.Key, state);
    }

    private void UpdateConsolesFor(EntityUid pod)
    {
        var query = EntityQueryEnumerator<DropPodConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (console.LinkedPod == pod)
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

    private void StartCountdown(Entity<DropPodComponent> ent, MapCoordinates target, EntityCoordinates? coords = null)
    {
        ent.Comp.Target = target;
        if (coords == null)
            ent.Comp.TargetGrid = null;

        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = Spawn(ent.Comp.MarkerPrototype, coords ?? _xform.ToCoordinates(target));

        SetState(ent, DropPodState.Countdown, ent.Comp.CountdownTime);
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

        CleanupFades();

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
                    SetZoom(ent, Lerp(1f, comp.PeakZoom, Smooth(t)));
                    if (now >= comp.StateEnd)
                        EnterBluespace(ent);
                    break;

                case DropPodState.InBluespace:
                    if (now >= comp.StateEnd)
                        Arrive(ent);
                    break;

                case DropPodState.Arriving:
                    if (now >= comp.StateEnd)
                        Land(ent);
                    break;

                case DropPodState.Landing:
                    if (now >= comp.StateEnd)
                        SetState(ent, DropPodState.Idle, TimeSpan.Zero);
                    break;
            }
        }
    }

    /// <summary>Снимает затемнение с тех, кто больше не в капсуле, или если капсула уничтожена / не летит.</summary>
    private void CleanupFades()
    {
        var fades = EntityQueryEnumerator<DropPodFadeComponent>();
        while (fades.MoveNext(out var uid, out var fade))
        {
            var valid = TryComp(fade.Pod, out DropPodComponent? pod) &&
                        !TerminatingOrDeleted(fade.Pod) &&
                        pod.State is DropPodState.ZoomingOut or DropPodState.InBluespace or DropPodState.Arriving &&
                        TryComp(fade.Pod, out EntityStorageComponent? storage) &&
                        storage.Contents.Contains(uid);

            if (!valid)
                RemCompDeferred<DropPodFadeComponent>(uid);
        }
    }

    private void UpdateCapture(Entity<DropPodConsoleComponent> console, TimeSpan now)
    {
        if (console.Comp.CapturedGrid is { } captured && TerminatingOrDeleted(captured))
        {
            console.Comp.CapturedGrid = null;
            UpdateConsole(console);
        }

        if (console.Comp.CapturingGrid is not { } grid)
            return;

        if (TerminatingOrDeleted(grid))
        {
            console.Comp.CapturingGrid = null;
            UpdateConsole(console, "droppod-error-bad-grid");
            return;
        }

        if (now < console.Comp.CaptureEnd)
            return;

        console.Comp.CapturedGrid = grid;
        console.Comp.CapturingGrid = null;
        UpdateConsole(console);
    }

    private void FinishCountdown(Entity<DropPodComponent> ent)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage) || storage.Open)
        {
            _popup.PopupEntity(Loc.GetString("droppod-fail-open"), ent, PopupType.MediumCaution);
            Abort(ent);
            return;
        }

        SetState(ent, DropPodState.ZoomingOut, ent.Comp.ZoomOutTime);
        SetFade(ent, 0f, 1f, ent.Comp.ZoomOutTime);
        _audio.PlayPvs(ent.Comp.LaunchSound, ent);
    }

    private void EnterBluespace(Entity<DropPodComponent> ent)
    {
        SpawnAtPosition(ent.Comp.LaunchEffect, Transform(ent).Coordinates);

        _appearance.SetData(ent, DropPodVisuals.InFlight, true);
        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, false, body: physics);

        // Под полной темнотой снимаем все эффекты: зум возвращается к обычному.
        ResetZoom(ent);
        SetFade(ent, 1f, 1f, TimeSpan.Zero);

        SetState(ent, DropPodState.InBluespace, ent.Comp.FlightTime);
    }

    /// <summary>Камера (вместе с невидимой капсулой) перебрасывается на место посадки, экран светлеет.</summary>
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

        // Капсула остаётся скрытой и без коллизий, пока экран не посветлеет.
        _xform.SetCoordinates(ent.Owner, coords);
        _xform.AttachToGridOrMap(ent.Owner);

        SetFade(ent, 1f, 0f, ent.Comp.FadeInTime);
        SetState(ent, DropPodState.Arriving, ent.Comp.FadeInTime);
    }

    /// <summary>Экран посветлел: капсула падает и появляется вместе с пассажирами.</summary>
    private void Land(Entity<DropPodComponent> ent)
    {
        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;
        ent.Comp.TargetGrid = null;

        ClearFade(ent);

        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, true, body: physics);
        _appearance.SetData(ent, DropPodVisuals.InFlight, false);

        SpawnAtPosition(ent.Comp.LandingEffect, Transform(ent).Coordinates);
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
    private EntityCoordinates? PickLandingTile(EntityUid gridUid)
    {
        if (!TryComp(gridUid, out MapGridComponent? grid))
            return null;

        var free = new List<Vector2i>();
        var any = new List<Vector2i>();
        foreach (var tile in _map.GetAllTiles(gridUid, grid))
        {
            any.Add(tile.GridIndices);
            if (IsTileFree((gridUid, grid), tile.GridIndices))
                free.Add(tile.GridIndices);
        }

        var pool = free.Count > 0 ? free : any;
        if (pool.Count == 0)
            return new EntityCoordinates(gridUid, Vector2.Zero);

        return _map.GridTileToLocal(gridUid, grid, _random.Pick(pool));
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
    private void SetFade(Entity<DropPodComponent> ent, float from, float to, TimeSpan duration)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            var fade = EnsureComp<DropPodFadeComponent>(occupant);
            fade.Pod = ent;
            fade.From = from;
            fade.To = to;
            fade.Start = _timing.CurTime;
            fade.End = _timing.CurTime + duration;
            Dirty(occupant, fade);
        }
    }

    private void ClearFade(Entity<DropPodComponent> ent)
    {
        if (!TryComp(ent, out EntityStorageComponent? storage))
            return;

        foreach (var occupant in storage.Contents.ContainedEntities)
        {
            RemCompDeferred<DropPodFadeComponent>(occupant);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    #endregion
}
