using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
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
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropPodComponent, StorageOpenAttemptEvent>(OnStorageOpenAttempt);
        SubscribeLocalEvent<DropPodComponent, ComponentShutdown>(OnPodShutdown);

        SubscribeLocalEvent<DropPodConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodLaunchMessage>(OnLaunchMessage);
        SubscribeLocalEvent<DropPodConsoleComponent, DropPodAbortMessage>(OnAbortMessage);
    }

    #region Pod events

    private void OnStorageOpenAttempt(Entity<DropPodComponent> ent, ref StorageOpenAttemptEvent args)
    {
        // Во время полёта дверь заблокирована.
        if (ent.Comp.State is DropPodState.ZoomingOut or DropPodState.InBluespace)
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

        DropPodConsoleState state;
        if (pod == null)
        {
            state = new DropPodConsoleState(false, DropPodState.Idle, TimeSpan.Zero, 0, origin, null,
                error == null ? null : Loc.GetString(error));
        }
        else
        {
            var comp = pod.Value.Comp;
            var occupants = TryComp(pod.Value, out EntityStorageComponent? storage)
                ? storage.Contents.ContainedEntities.Count
                : 0;
            Vector2? target = comp.State == DropPodState.Idle ? null : comp.Target.Position;

            state = new DropPodConsoleState(true, comp.State, comp.StateEnd, occupants, origin, target,
                error == null ? null : Loc.GetString(error));
        }

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
        UpdateConsolesFor(ent);
    }

    private void StartCountdown(Entity<DropPodComponent> ent, MapCoordinates target)
    {
        ent.Comp.Target = target;

        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = Spawn(ent.Comp.MarkerPrototype, _xform.ToCoordinates(target));

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
                    SetZoom(ent, comp.PeakZoom);
                    if (now >= comp.StateEnd)
                        Land(ent);
                    break;

                case DropPodState.Landing:
                    SetZoom(ent, Lerp(comp.PeakZoom, 1f, Smooth(t)));
                    if (now >= comp.StateEnd)
                        FinishLanding(ent);
                    break;
            }
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

        SetState(ent, DropPodState.ZoomingOut, ent.Comp.ZoomOutTime);
        _audio.PlayPvs(ent.Comp.LaunchSound, ent);
    }

    private void EnterBluespace(Entity<DropPodComponent> ent)
    {
        SpawnAtPosition(ent.Comp.LaunchEffect, Transform(ent).Coordinates);

        _appearance.SetData(ent, DropPodVisuals.InFlight, true);
        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, false, body: physics);

        SetState(ent, DropPodState.InBluespace, ent.Comp.FlightTime);
    }

    private void Land(Entity<DropPodComponent> ent)
    {
        // Метка могла уехать вместе с гридом - приземляемся туда, где она сейчас.
        var landing = ent.Comp.Target;
        if (ent.Comp.Marker is { } marker && !TerminatingOrDeleted(marker))
            landing = _xform.GetMapCoordinates(marker);

        QueueDel(ent.Comp.Marker);
        ent.Comp.Marker = null;

        _xform.SetMapCoordinates(ent.Owner, landing);

        if (TryComp(ent, out PhysicsComponent? physics))
            _physics.SetCanCollide(ent, true, body: physics);
        _appearance.SetData(ent, DropPodVisuals.InFlight, false);

        SpawnAtPosition(ent.Comp.LandingEffect, Transform(ent).Coordinates);
        SpawnAtPosition(ent.Comp.LaunchEffect, Transform(ent).Coordinates);
        _audio.PlayPvs(ent.Comp.LandingSound, ent);

        if (!ent.Comp.LandingDamage.Empty)
        {
            foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(landing, ent.Comp.LandingRadius))
            {
                if (mob.Owner == ent.Owner)
                    continue;

                _damageable.TryChangeDamage(mob.Owner, ent.Comp.LandingDamage, origin: ent.Owner);
            }
        }

        SetState(ent, DropPodState.Landing, ent.Comp.ZoomInTime);
    }

    private void FinishLanding(Entity<DropPodComponent> ent)
    {
        SetZoom(ent, 1f);
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

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    #endregion
}
