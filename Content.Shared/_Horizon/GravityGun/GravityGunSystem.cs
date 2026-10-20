using System.Numerics;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Horizon.GravityGun;

/// <summary>
/// Handles <see cref="GravityGunComponent"/>.
/// </summary>
public sealed class GravityGunSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<GravityGunComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GravityGunComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnUseInHand(Entity<GravityGunComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        ent.Comp.Mode = ent.Comp.Mode switch
        {
            GravityGunMode.Pull => GravityGunMode.Push,
            GravityGunMode.Push => GravityGunMode.Fire,
            _ => GravityGunMode.Pull,
        };
        Dirty(ent);

        _popup.PopupClient(Loc.GetString("gravity-gun-mode-" + ent.Comp.Mode.ToString().ToLowerInvariant()), ent, args.User);
    }

    private void OnAfterInteract(Entity<GravityGunComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        var user = args.User;
        if (target == user || target == ent.Owner)
            return;

        if (!_physicsQuery.TryComp(user, out var userPhysics))
            return;

        if (!_transform.InRange(Transform(user).Coordinates, Transform(target).Coordinates, ent.Comp.MaxDistance))
            return;

        if (!_useDelay.TryResetDelay(ent, checkDelayed: true))
            return;

        args.Handled = true;

        var userPos = _transform.GetWorldPosition(user);
        var targetPos = _transform.GetWorldPosition(target);
        var direction = targetPos - userPos; // user -> target
        if (direction.LengthSquared() < 0.0001f)
            return;
        direction = Vector2.Normalize(direction);

        if (CanMove(ent.Comp, target, out var targetPhysics))
            MoveTarget(ent.Comp, target, targetPhysics, direction);
        else
            MoveUser(ent.Comp, user, userPhysics, direction);

        _audio.PlayPredicted(ent.Comp.Sound, ent, user);
    }

    /// <summary>
    /// A target can be moved if it is a loose, light body.
    /// </summary>
    private bool CanMove(GravityGunComponent gun, EntityUid target, out PhysicsComponent physics)
    {
        if (!_physicsQuery.TryComp(target, out var body) ||
            body.BodyType != BodyType.Dynamic ||
            Transform(target).Anchored ||
            _container.IsEntityInContainer(target) ||
            body.Mass > gun.MassLimit ||
            !gun.CanAffectMobs && HasComp<MobStateComponent>(target))
        {
            physics = default!;
            return false;
        }

        physics = body;
        return true;
    }

    private void MoveTarget(GravityGunComponent gun, EntityUid target, PhysicsComponent physics, Vector2 towardsTarget)
    {
        // Velocity based, so a light and a heavy (but still movable) target get the same speed.
        var (dir, speed) = gun.Mode switch
        {
            GravityGunMode.Pull => (-towardsTarget, gun.MoveSpeed),
            GravityGunMode.Push => (towardsTarget, gun.MoveSpeed),
            _ => (towardsTarget, gun.FireSpeed),
        };

        _physics.ApplyLinearImpulse(target, dir * speed * physics.Mass, body: physics);
    }

    /// <summary>
    /// The target is too heavy to move (a grid or something anchored to it), so only the user is moved. The target is left alone.
    /// </summary>
    private void MoveUser(GravityGunComponent gun, EntityUid user, PhysicsComponent userPhysics, Vector2 towardsTarget)
    {
        var (dir, strength) = gun.Mode switch
        {
            GravityGunMode.Pull => (towardsTarget, gun.Power),
            GravityGunMode.Push => (-towardsTarget, gun.Power * gun.PushRecoilMultiplier),
            _ => (-towardsTarget, gun.Power * gun.FireRecoilMultiplier),
        };

        // Clamp by the resulting speed so a light user doesn't get launched out of the map.
        var mass = MathF.Max(userPhysics.Mass, 1f);
        var speed = MathF.Min(strength / mass, gun.MaxUserSpeed);
        var impulse = dir * speed * mass;

        _physics.ApplyLinearImpulse(user, impulse, body: userPhysics);
    }
}
