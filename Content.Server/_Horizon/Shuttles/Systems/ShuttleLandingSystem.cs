using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared._Horizon.Shuttles.Events;
using Content.Shared._Horizon.Shuttles.Systems;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shuttles.Systems;

public sealed class ShuttleLandingSystem : SharedShuttleLandingSystem
{
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ShuttleConsoleSystem _console = default!;

    /// <summary>Fraction of the shuttle bounds that must lie over the landing zone to land.</summary>
    private const float MinLandingOverlap = 0.5f;
    private const int GridBit = (int) CollisionGroup.MapGrid;
    private TimeSpan _nextMaskCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, ShuttleLandingRequest>(OnLandingRequest);
    }

    private void OnLandingRequest(EntityUid uid, ShuttleConsoleComponent component, ShuttleLandingRequest args)
    {
        if (Transform(uid).GridUid is not { } grid)
            return;

        string? failure = null;

        if (args.TakeOff)
        {
            if (!TryTakeOff(grid))
                failure = "shuttle-landing-fail-not-landed";
        }
        else if (!TryLand(grid, out var reason))
        {
            failure = reason;
        }

        if (failure != null)
            _popup.PopupEntity(Loc.GetString(failure), uid, args.Actor);
        else
            _console.RefreshShuttleConsoles(grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextMaskCheck)
            return;

        _nextMaskCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        // Grid fixtures are rebuilt when tiles change, which resets their masks, so keep re-applying.
        var shuttles = EntityQueryEnumerator<LandableShuttleComponent, FixturesComponent>();
        while (shuttles.MoveNext(out var uid, out _, out var fixtures))
            IgnoreGridCollision(uid, fixtures);

        var pads = EntityQueryEnumerator<LandingPadComponent, FixturesComponent>();
        while (pads.MoveNext(out var uid, out _, out var fixtures))
            IgnoreGridCollision(uid, fixtures);
    }

    /// <summary>
    /// Removes the grid-vs-grid bit from the collision mask of the grid fixtures so that tiles can overlap.
    /// Wall and other entity fixtures are untouched and keep colliding.
    /// </summary>
    private void IgnoreGridCollision(EntityUid uid, FixturesComponent fixtures)
    {
        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            if ((fixture.CollisionLayer & GridBit) == 0 || (fixture.CollisionMask & GridBit) == 0)
                continue;

            _physics.SetCollisionMask(uid, id, fixture, fixture.CollisionMask & ~GridBit, fixtures);
        }
    }

    /// <summary>
    /// Tries to land the shuttle on the landing pad grid it overlaps.
    /// </summary>
    public bool TryLand(Entity<LandableShuttleComponent?> shuttle, out string reason)
    {
        reason = string.Empty;

        if (!Resolve(shuttle, ref shuttle.Comp, false) ||
            !TryComp<MapGridComponent>(shuttle, out var shuttleGrid) ||
            !TryComp<PhysicsComponent>(shuttle, out var body))
        {
            reason = "shuttle-landing-fail-not-landable";
            return false;
        }

        if (shuttle.Comp.State == LandState.Landed)
        {
            reason = "shuttle-landing-fail-already-landed";
            return false;
        }

        if (body.LinearVelocity.Length() > shuttle.Comp.MaxLandingSpeed)
        {
            reason = "shuttle-landing-fail-too-fast";
            return false;
        }

        var shuttleXform = Transform(shuttle);
        var shuttleMatrix = _transform.GetWorldMatrix(shuttleXform);
        var corners = new Box2Rotated(shuttleGrid.LocalAABB, Angle.Zero);

        var pads = EntityQueryEnumerator<LandingPadComponent, MapGridComponent, TransformComponent>();
        while (pads.MoveNext(out var padUid, out var pad, out var padGrid, out var padXform))
        {
            if (padUid == shuttle.Owner || padXform.MapID != shuttleXform.MapID)
                continue;

            var zone = pad.Zone ?? padGrid.LocalAABB;
            var toPad = _transform.GetInvWorldMatrix(padXform) * shuttleMatrix;

            // Bounds of the shuttle in the pad's local space; at least half of it must lie over the landing zone.
            var shuttleBounds = new Box2(
                Vector2.Transform(corners.BottomLeft, toPad),
                Vector2.Transform(corners.TopRight, toPad));
            var min = Vector2.Min(Vector2.Min(Vector2.Transform(corners.BottomLeft, toPad), Vector2.Transform(corners.BottomRight, toPad)),
                Vector2.Min(Vector2.Transform(corners.TopLeft, toPad), Vector2.Transform(corners.TopRight, toPad)));
            var max = Vector2.Max(Vector2.Max(Vector2.Transform(corners.BottomLeft, toPad), Vector2.Transform(corners.BottomRight, toPad)),
                Vector2.Max(Vector2.Transform(corners.TopLeft, toPad), Vector2.Transform(corners.TopRight, toPad)));
            shuttleBounds = new Box2(min, max);

            var overlap = zone.Intersect(shuttleBounds);
            if (overlap.IsEmpty() || overlap.Width * overlap.Height < shuttleBounds.Width * shuttleBounds.Height * MinLandingOverlap)
                continue;

            _physics.SetLinearVelocity(shuttle, Vector2.Zero, body: body);
            _physics.SetAngularVelocity(shuttle, 0f, body: body);
            _shuttle.Disable(shuttle);

            shuttle.Comp.State = LandState.Landed;
            shuttle.Comp.Pad = padUid;
            Dirty(shuttle);
            return true;
        }

        reason = "shuttle-landing-fail-no-zone";
        return false;
    }

    public bool TryTakeOff(Entity<LandableShuttleComponent?> shuttle)
    {
        if (!Resolve(shuttle, ref shuttle.Comp, false) || shuttle.Comp.State != LandState.Landed)
            return false;

        _shuttle.Enable(shuttle);
        shuttle.Comp.State = LandState.Flying;
        shuttle.Comp.Pad = null;
        Dirty(shuttle);
        return true;
    }
}
