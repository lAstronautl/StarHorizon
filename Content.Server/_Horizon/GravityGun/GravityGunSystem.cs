using System.Numerics;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Horizon.GravityGun;

/// <summary>
/// Handles <see cref="GravityGunComponent"/>: grabbing a loose entity or a grid, holding it where the user aims,
/// and moving the user instead when the target is too heavy.
/// </summary>
public sealed class GravityGunSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private const string TetherJoint = "gravity-gun-tether";

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<GravityGunComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<GravityGunComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GravityGunComponent, ComponentShutdown>(OnShutdown);

        SubscribeNetworkEvent<GravityGunAimEvent>(OnAim);
        SubscribeNetworkEvent<GravityGunDistanceEvent>(OnDistance);
    }

    private void OnShutdown(Entity<GravityGunComponent> ent, ref ComponentShutdown args)
    {
        // Everything may be going away at once: the joint goes with the tether body, nothing is touched.
        ent.Comp.Held = null;
        if (ent.Comp.Tether is { } tether && !TerminatingOrDeleted(tether))
            QueueDel(tether);

        ent.Comp.Tether = null;
    }

    private void OnUseInHand(Entity<GravityGunComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Held == null)
            return;

        args.Handled = true;
        Release(ent);
    }

    private void OnAfterInteract(Entity<GravityGunComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !_useDelay.TryResetDelay(ent, checkDelayed: true))
            return;

        args.Handled = true;

        // Already holding something: launch it where the user is aiming.
        if (ent.Comp.Held != null)
        {
            Fire(ent, args.User);
            return;
        }

        TryGrab(ent, args.User, args.Target, args.ClickLocation);
    }

    private void TryGrab(Entity<GravityGunComponent> ent, EntityUid user, EntityUid? target, EntityCoordinates click)
    {
        var gun = ent.Comp;
        var clickPos = _transform.ToMapCoordinates(click);
        var userPos = _transform.GetWorldPosition(user);

        if ((clickPos.Position - userPos).Length() > gun.MaxDistance)
            return;

        if (!TryPick(gun, user, target, clickPos, out var held, out var heavy))
        {
            _popup.PopupEntity(Loc.GetString("gravity-gun-nothing"), ent, user);
            return;
        }

        gun.Held = held;
        gun.HeldPoint = Vector2.Transform(clickPos.Position, _transform.GetInvWorldMatrix(held));
        gun.HoldDistance = Math.Clamp((clickPos.Position - userPos).Length(), gun.MinHoldDistance, gun.MaxDistance);
        gun.UserIsMoved = heavy;
        gun.Aim = Vector2.Normalize(clickPos.Position - userPos);
        Dirty(ent);

        if (!heavy)
            StartTether(gun, held, clickPos);

        _audio.PlayPvs(gun.Sound, ent);
    }

    /// <summary>
    /// Works out what the click grabs. A loose, light entity is grabbed itself; something anchored to a grid grabs the
    /// whole grid. What is too heavy to move (or the user's own grid) is still grabbed, but moves the user instead.
    /// </summary>
    private bool TryPick(GravityGunComponent gun, EntityUid user, EntityUid? target, MapCoordinates click, out EntityUid held, out bool heavy)
    {
        held = default;
        heavy = false;

        // Clicking the floor targets the grid under the cursor.
        if (target == null)
        {
            if (!_mapManager.TryFindGridAt(click, out var gridUnder, out _))
                return false;

            target = gridUnder;
        }

        var xform = Transform(target.Value);
        var isGrid = HasComp<MapGridComponent>(target.Value);

        if (!isGrid && _physicsQuery.TryComp(target.Value, out var body) &&
            body.BodyType == BodyType.Dynamic && !xform.Anchored && !_container.IsEntityInContainer(target.Value))
        {
            if (!gun.CanAffectMobs && HasComp<MobStateComponent>(target.Value))
                return false;

            held = target.Value;
            heavy = body.Mass > gun.MassLimit;
            return true;
        }

        // Anchored to a grid, or standing on one: the grid is what gets grabbed.
        var gridUid = isGrid ? target.Value : xform.GridUid;
        if (gridUid == null || !_physicsQuery.TryComp(gridUid.Value, out var gridBody))
            return false;

        held = gridUid.Value;
        heavy = gridBody.BodyType != BodyType.Dynamic ||
                gridBody.Mass > gun.GridMassLimit ||
                Transform(user).GridUid == gridUid;
        return true;
    }

    private void Release(Entity<GravityGunComponent> ent)
    {
        if (ent.Comp.Held == null)
            return;

        if (_physicsQuery.TryComp(ent.Comp.Held, out var body))
            _physics.SetSleepingAllowed(ent.Comp.Held.Value, body, true);

        ent.Comp.Held = null;
        RemoveTether(ent.Comp);
        Dirty(ent);
    }

    /// <summary>
    /// Joins the target to an invisible body with a mouse joint, the way the tether gun does, so the physics engine
    /// does the moving and a heavy target is slow to bring around.
    /// </summary>
    private void StartTether(GravityGunComponent gun, EntityUid held, MapCoordinates click)
    {
        if (!_physicsQuery.TryComp(held, out var body))
            return;

        _physics.WakeBody(held, body: body);
        _physics.SetSleepingAllowed(held, body, false);

        var tether = Spawn("TetherEntity", click);
        var tetherBody = _physicsQuery.GetComponent(tether);
        _physics.WakeBody(tether, body: tetherBody);

        var joint = _joints.CreateMouseJoint(tether, held, id: TetherJoint);
        SharedJointSystem.LinearStiffness(gun.Frequency, gun.DampingRatio, tetherBody.Mass, body.Mass, out var stiffness, out var damping);
        joint.Stiffness = stiffness;
        joint.Damping = damping;
        joint.MaxForce = gun.MaxForce;

        gun.Tether = tether;
    }

    private void RemoveTether(GravityGunComponent gun)
    {
        if (gun.Tether is not { } tether)
            return;

        gun.Tether = null;
        if (TerminatingOrDeleted(tether))
            return;

        _joints.RemoveJoint(tether, TetherJoint);
        QueueDel(tether);
    }

    private void Fire(Entity<GravityGunComponent> ent, EntityUid user)
    {
        var gun = ent.Comp;
        if (gun.Held is not { } held || !_physicsQuery.TryComp(held, out var body))
        {
            Release(ent);
            return;
        }

        var dir = gun.Aim;
        RemoveTether(gun);
        if (gun.UserIsMoved)
        {
            // Launching at something that will not budge: the user is thrown back, the target stays put.
            if (_physicsQuery.TryComp(user, out var userBody))
                ApplyUserImpulse(gun, user, userBody, -dir, gun.Power * gun.FireRecoilMultiplier);
        }
        else
        {
            _physics.ApplyLinearImpulse(held, dir * gun.FireSpeed * body.Mass, body: body);
        }

        _audio.PlayPvs(gun.Sound, ent);
        Release(ent);
    }

    /// <summary>
    /// Gives the user a push, clamped by the resulting speed so a light user doesn't get launched out of the map.
    /// </summary>
    private void ApplyUserImpulse(GravityGunComponent gun, EntityUid user, PhysicsComponent userBody, Vector2 dir, float strength)
    {
        var mass = MathF.Max(userBody.Mass, 1f);
        var speed = MathF.Min(strength / mass, gun.MaxUserSpeed);
        _physics.ApplyLinearImpulse(user, dir * speed * mass, body: userBody);
    }

    private bool TryGetHeldGun(ICommonSession session, out Entity<GravityGunComponent> gun, out EntityUid user)
    {
        gun = default;
        user = default;

        if (session.AttachedEntity is not { } attached ||
            !_hands.TryGetActiveItem(attached, out var item) ||
            !TryComp<GravityGunComponent>(item, out var comp) ||
            comp.Held == null)
            return false;

        user = attached;
        gun = (item.Value, comp);
        return true;
    }

    private void OnAim(GravityGunAimEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetHeldGun(args.SenderSession, out var gun, out var user))
            return;

        var aim = _transform.ToMapCoordinates(GetCoordinates(msg.Coordinates)).Position - _transform.GetWorldPosition(user);
        if (aim.LengthSquared() > 0.01f)
            gun.Comp.Aim = Vector2.Normalize(aim);
    }

    private void OnDistance(GravityGunDistanceEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetHeldGun(args.SenderSession, out var gun, out _))
            return;

        var comp = gun.Comp;
        comp.HoldDistance = Math.Clamp(comp.HoldDistance + Math.Sign(msg.Delta) * comp.WheelStep, comp.MinHoldDistance, comp.MaxDistance);
        Dirty(gun);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GravityGunComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gun, out var xform))
        {
            if (gun.Held is not { } held)
                continue;

            var user = xform.ParentUid;
            if (TerminatingOrDeleted(held) || !Exists(user) || !_hands.IsHolding(user, uid) ||
                _container.IsEntityInContainer(held))
            {
                Release((uid, gun));
                continue;
            }

            var userPos = _transform.GetWorldPosition(user);
            var heldPoint = Vector2.Transform(gun.HeldPoint, _transform.GetWorldMatrix(held));

            // The target got out of reach (or the user floated away from it).
            if ((heldPoint - userPos).Length() > gun.MaxDistance * 1.5f)
            {
                Release((uid, gun));
                continue;
            }

            if (gun.UserIsMoved)
                PullUser(gun, user, userPos, heldPoint, frameTime);
            else
                MoveHeld(gun, held, userPos, frameTime);
        }
    }

    /// <summary>
    /// Moves the tether body to where the user is aiming, at the held distance; the joint brings the target along.
    /// </summary>
    private void MoveHeld(GravityGunComponent gun, EntityUid held, Vector2 userPos, float frameTime)
    {
        if (gun.Tether is not { } tether || !_physicsQuery.TryComp(held, out var body))
            return;

        _transform.SetWorldPosition(tether, userPos + gun.Aim * gun.HoldDistance);

        // Don't let a held grid spin about.
        _physics.SetAngularVelocity(held, body.AngularVelocity * (1f - MathF.Min(1f, frameTime * 3f)), body: body);
    }

    /// <summary>
    /// The target will not move, so the user is moved to the held distance from it. The wheel opens or closes it.
    /// </summary>
    private void PullUser(GravityGunComponent gun, EntityUid user, Vector2 userPos, Vector2 heldPoint, float frameTime)
    {
        if (!_physicsQuery.TryComp(user, out var userBody))
            return;

        var delta = heldPoint - userPos;
        var distance = delta.Length();
        if (distance < 0.01f)
            return;

        var dir = delta / distance;
        var speed = Math.Clamp((distance - gun.HoldDistance) * gun.Gain, -gun.MaxUserSpeed, gun.MaxUserSpeed);
        var impulse = (dir * speed - userBody.LinearVelocity) * userBody.Mass * MathF.Min(1f, frameTime * gun.Gain);
        var limit = gun.UserForce * frameTime;
        if (impulse.Length() > limit)
            impulse = Vector2.Normalize(impulse) * limit;

        _physics.ApplyLinearImpulse(user, impulse, body: userBody);
    }
}
