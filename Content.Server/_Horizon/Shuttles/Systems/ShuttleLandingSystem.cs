using System.Numerics;
using Content.Server._Horizon.Shuttles.Components;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Horizon.CCVar;
using Content.Shared._Horizon.Shuttles.Events;
using Content.Shared.Body.Components;
using Content.Shared.Database;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.SubFloor;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shuttles.Systems;

/// <summary>
/// Lets a shuttle land on the landing pad tiles of another grid (tiles with
/// <see cref="ContentTileDefinition.GridUnderlay"/>) and take off again.
/// A landed shuttle is welded to the grid under it, so it moves and jumps to FTL together with it.
/// </summary>
public sealed class ShuttleLandingSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleConsoleSystem _console = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;

    private const string JointPrefix = "landing";

    /// <summary>
    /// How often landed shuttles are checked for a missing host.
    /// </summary>
    private const float CheckInterval = 1f;

    /// <summary>
    /// What to do with whatever is on the pad under a landing shuttle.
    /// </summary>
    private enum OccupiedMode : byte
    {
        /// <summary>Nothing is checked, things on the pad end up inside the shuttle.</summary>
        None,

        /// <summary>The shuttle cannot land on a living mob or an anchored structure.</summary>
        Block,

        /// <summary>Landing destroys everything under the shuttle, like arriving from FTL on a planet does.</summary>
        Crush,
    }

    private float _minCoverage;
    private float _maxSpeed;
    private OccupiedMode _occupiedMode;
    private float _checkAccumulator;

    private readonly HashSet<EntityUid> _lookupEnts = new();
    private readonly List<EntityUid> _under = new();

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<(EntityUid Uid, Matrix3x2 InvMatrix, MapGridComponent Grid)> _candidates = new();
    private readonly Dictionary<EntityUid, int> _coverage = new();
    private readonly List<EntityUid> _toProcess = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleConsoleComponent, ShuttleConsoleLandingRequest>(OnLandingRequest);

        SubscribeLocalEvent<LandedShuttleComponent, ComponentShutdown>(OnLandedShutdown);
        SubscribeLocalEvent<LandedShuttleComponent, ConsoleFTLAttemptEvent>(OnFTLAttempt);
        SubscribeLocalEvent<LandedShuttleComponent, TileFrictionEvent>(OnTileFriction);

        SubscribeLocalEvent<FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);

        Subs.CVar(_cfg, HorizonCCVars.ShuttleLandingMinCoverage, value => _minCoverage = value, true);
        Subs.CVar(_cfg, HorizonCCVars.ShuttleLandingMaxSpeed, value => _maxSpeed = value, true);
        Subs.CVar(_cfg, HorizonCCVars.ShuttleLandingOccupiedMode, SetOccupiedMode, true);
    }

    private void SetOccupiedMode(string value)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
                _occupiedMode = OccupiedMode.None;
                break;
            case "crush":
                _occupiedMode = OccupiedMode.Crush;
                break;
            case "block":
                _occupiedMode = OccupiedMode.Block;
                break;
            default:
                Log.Warning($"Unknown value '{value}' of {HorizonCCVars.ShuttleLandingOccupiedMode.Name}, expected none, block or crush. Using block.");
                _occupiedMode = OccupiedMode.Block;
                break;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _checkAccumulator += frameTime;
        if (_checkAccumulator < CheckInterval)
            return;

        _checkAccumulator = 0f;
        _toProcess.Clear();

        var query = EntityQueryEnumerator<LandedShuttleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var landed, out var xform))
        {
            // The host is gone, or it went to another map without us.
            if (TerminatingOrDeleted(landed.Host) || Transform(landed.Host).MapUid != xform.MapUid)
                _toProcess.Add(uid);
        }

        foreach (var uid in _toProcess)
        {
            if (!TryComp<LandedShuttleComponent>(uid, out var landed))
                continue;

            if (TerminatingOrDeleted(landed.Host))
                TakeOff(uid);
            else
                PlaceOnHost((uid, landed));
        }
    }

    #region Public API

    /// <summary>
    /// Is this shuttle sitting on a landing pad?
    /// </summary>
    public bool IsLanded(EntityUid shuttle)
    {
        return HasComp<LandedShuttleComponent>(shuttle);
    }

    /// <summary>
    /// Lands the shuttle on the landing pad under it.
    /// </summary>
    /// <param name="reason">Why the shuttle cannot land, already localized.</param>
    public bool TryLand(EntityUid shuttle, out string? reason)
    {
        reason = null;

        if (HasComp<LandedShuttleComponent>(shuttle))
        {
            reason = Loc.GetString("shuttle-landing-already-landed");
            return false;
        }

        if (HasComp<FTLComponent>(shuttle))
        {
            reason = Loc.GetString("shuttle-landing-in-ftl");
            return false;
        }

        if (!TryComp<MapGridComponent>(shuttle, out var grid) ||
            !TryComp<PhysicsComponent>(shuttle, out var body))
        {
            reason = Loc.GetString("shuttle-landing-no-pad");
            return false;
        }

        if (!TryFindHost((shuttle, grid), out var host))
        {
            reason = Loc.GetString("shuttle-landing-no-pad");
            return false;
        }

        var shuttleXform = Transform(shuttle);
        var hostVelocity = GetHostVelocityAt(host, _transform.GetWorldPosition(shuttleXform));
        var relativeSpeed = (_physics.GetMapLinearVelocity(shuttle, body, shuttleXform) - hostVelocity).Length();

        if (relativeSpeed > _maxSpeed)
        {
            reason = Loc.GetString("shuttle-landing-too-fast");
            return false;
        }

        if (_occupiedMode != OccupiedMode.None)
        {
            GetEntitiesUnder((shuttle, grid), host, _under);

            if (_occupiedMode == OccupiedMode.Block && IsBlocked(_under))
            {
                reason = Loc.GetString("shuttle-landing-occupied");
                return false;
            }

            if (_occupiedMode == OccupiedMode.Crush)
                Crush(shuttle, _under);
        }

        Land(shuttle, host);
        return true;
    }

    /// <summary>
    /// Releases a landed shuttle so it can be piloted again.
    /// </summary>
    public bool TakeOff(EntityUid shuttle)
    {
        // The weld is removed when the component shuts down.
        return RemComp<LandedShuttleComponent>(shuttle);
    }

    #endregion

    #region Landing

    private void OnLandingRequest(EntityUid uid, ShuttleConsoleComponent component, ShuttleConsoleLandingRequest args)
    {
        if (Transform(uid).GridUid is not { } shuttle || !HasComp<ShuttleComponent>(shuttle))
            return;

        if (!args.Land)
        {
            if (TakeOff(shuttle))
                _popup.PopupEntity(Loc.GetString("shuttle-landing-took-off"), uid, args.Actor);

            return;
        }

        if (TryLand(shuttle, out var reason))
            _popup.PopupEntity(Loc.GetString("shuttle-landing-landed"), uid, args.Actor);
        else if (reason != null)
            _popup.PopupEntity(reason, uid, args.Actor, PopupType.MediumCaution);
    }

    /// <summary>
    /// Finds the grid whose landing pad tiles are under enough of the shuttle's tiles.
    /// </summary>
    private bool TryFindHost(Entity<MapGridComponent> shuttle, out EntityUid host)
    {
        host = EntityUid.Invalid;
        var xform = Transform(shuttle);

        if (xform.MapUid == null)
            return false;

        var shuttleMatrix = _transform.GetWorldMatrix(xform);
        var worldAABB = shuttleMatrix.TransformBox(shuttle.Comp.LocalAABB);

        _grids.Clear();
        _candidates.Clear();
        _coverage.Clear();
        _mapManager.FindGridsIntersecting(xform.MapUid.Value, worldAABB, ref _grids, approx: true, includeMap: false);

        foreach (var grid in _grids)
        {
            // Not ourselves, and not something that is sitting on us.
            if (grid.Owner == shuttle.Owner || IsLandedOn(grid.Owner, shuttle.Owner))
                continue;

            _candidates.Add((grid.Owner, _transform.GetInvWorldMatrix(grid.Owner), grid.Comp));
        }

        if (_candidates.Count == 0)
            return false;

        var total = 0;
        var tileSize = shuttle.Comp.TileSize;

        foreach (var tile in _map.GetAllTiles(shuttle.Owner, shuttle.Comp))
        {
            total++;
            var local = (new Vector2(tile.GridIndices.X, tile.GridIndices.Y) + new Vector2(0.5f, 0.5f)) * tileSize;
            var world = Vector2.Transform(local, shuttleMatrix);

            foreach (var (candidate, invMatrix, candidateGrid) in _candidates)
            {
                var candidateLocal = Vector2.Transform(world, invMatrix) / candidateGrid.TileSize;
                var indices = new Vector2i((int) MathF.Floor(candidateLocal.X), (int) MathF.Floor(candidateLocal.Y));

                if (!_map.TryGetTileRef(candidate, candidateGrid, indices, out var candidateTile) ||
                    !IsLandingPad(candidateTile.Tile))
                {
                    continue;
                }

                _coverage[candidate] = _coverage.GetValueOrDefault(candidate) + 1;
                break;
            }
        }

        if (total == 0)
            return false;

        var best = 0;

        foreach (var (candidate, count) in _coverage)
        {
            if (count <= best)
                continue;

            best = count;
            host = candidate;
        }

        return best > 0 && (float) best / total >= _minCoverage;
    }

    /// <summary>
    /// Collects what is on the host under the shuttle: entities of the host whose position is over one of the shuttle's tiles.
    /// Things under the floor (cables, pipes) and things immune to an FTL arrival are left out.
    /// </summary>
    private void GetEntitiesUnder(Entity<MapGridComponent> shuttle, EntityUid host, List<EntityUid> under)
    {
        under.Clear();
        _lookupEnts.Clear();

        var xform = Transform(shuttle);
        var worldAABB = _transform.GetWorldMatrix(xform).TransformBox(shuttle.Comp.LocalAABB);
        var invMatrix = _transform.GetInvWorldMatrix(xform);
        var tileSize = shuttle.Comp.TileSize;

        _lookup.GetEntitiesIntersecting(xform.MapID, worldAABB, _lookupEnts, LookupFlags.Uncontained);

        foreach (var ent in _lookupEnts)
        {
            if (ent == shuttle.Owner || ent == host)
                continue;

            var entXform = Transform(ent);

            if (entXform.GridUid != host ||
                HasComp<SubFloorHideComponent>(ent) ||
                HasComp<FTLSmashImmuneComponent>(ent))
            {
                continue;
            }

            // The box above is rough, so check the tile the entity is actually over.
            var local = Vector2.Transform(_transform.GetWorldPosition(entXform), invMatrix) / tileSize;
            var indices = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));

            if (!_map.TryGetTileRef(shuttle.Owner, shuttle.Comp, indices, out var tile) || tile.Tile.IsEmpty)
                continue;

            under.Add(ent);
        }
    }

    /// <summary>
    /// Is there something under the shuttle that it must not land on: a living mob or an anchored solid structure?
    /// Loose items and corpses do not count.
    /// </summary>
    private bool IsBlocked(List<EntityUid> under)
    {
        foreach (var ent in under)
        {
            if (TryComp<MobStateComponent>(ent, out var mobState))
            {
                if (!_mobState.IsDead(ent, mobState))
                    return true;

                continue;
            }

            if (Transform(ent).Anchored &&
                TryComp<PhysicsComponent>(ent, out var entBody) &&
                entBody.CanCollide &&
                entBody.Hard)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Destroys everything under the landing shuttle, the same way an FTL arrival does.
    /// </summary>
    private void Crush(EntityUid shuttle, List<EntityUid> under)
    {
        // Gibbing a body spawns its parts, and those must not be handled a second time.
        _lookupEnts.Clear();

        foreach (var ent in under)
        {
            if (_lookupEnts.Contains(ent) || TerminatingOrDeleted(ent))
                continue;

            if (TryComp<BodyComponent>(ent, out var body))
            {
                _adminLog.Add(LogType.Gib,
                    LogImpact.Extreme,
                    $"{ToPrettyString(ent):player} got gibbed by the shuttle {ToPrettyString(shuttle)} landing on a landing pad at {Transform(ent).Coordinates:coordinates}");

                _lookupEnts.UnionWith(_body.GibBody(ent, body: body));
                continue;
            }

            QueueDel(ent);
        }
    }

    private bool IsLandingPad(Tile tile)
    {
        return !tile.IsEmpty &&
               _tileDefs.TryGetDefinition(tile.TypeId, out var def) &&
               def is ContentTileDefinition { GridUnderlay: true };
    }

    /// <summary>
    /// Is <paramref name="uid"/> landed on <paramref name="host"/>, directly or through other landed shuttles?
    /// </summary>
    private bool IsLandedOn(EntityUid uid, EntityUid host)
    {
        // The depth limit only guards against a broken chain.
        for (var i = 0; i < 16 && TryComp<LandedShuttleComponent>(uid, out var landed); i++)
        {
            if (landed.Host == host)
                return true;

            uid = landed.Host;
        }

        return false;
    }

    private void Land(EntityUid shuttle, EntityUid host)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(shuttle);
        var landed = AddComp<LandedShuttleComponent>(shuttle);

        landed.Host = host;
        landed.JointId = JointPrefix + shuttle;
        landed.LocalPosition = Vector2.Transform(position, _transform.GetInvWorldMatrix(host));
        landed.LocalRotation = rotation - _transform.GetWorldRotation(host);

        MatchHostVelocity(shuttle, host);
        Weld((shuttle, landed));

        if (TryComp<ShuttleComponent>(shuttle, out var shuttleComp))
        {
            _thruster.DisableLinearThrusters(shuttleComp);
            _thruster.SetAngularThrust(shuttleComp, false);
        }

        _console.RefreshShuttleConsoles(shuttle);
    }

    private void OnLandedShutdown(Entity<LandedShuttleComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        RemoveWeld(ent);
        _console.RefreshShuttleConsoles(ent);
    }

    /// <summary>
    /// Welds the shuttle to its host at their current positions.
    /// </summary>
    private void Weld(Entity<LandedShuttleComponent> shuttle)
    {
        var host = shuttle.Comp.Host;

        if (!TryComp<PhysicsComponent>(host, out var hostBody) ||
            !TryComp<PhysicsComponent>(shuttle, out var shuttleBody) ||
            Transform(host).MapID != Transform(shuttle).MapID)
        {
            return;
        }

        // Same spring as docking uses.
        SharedJointSystem.LinearStiffness(
            2f,
            0.7f,
            hostBody.Mass,
            shuttleBody.Mass,
            out var stiffness,
            out var damping);

        var anchorWorld = _transform.GetWorldPosition(shuttle);
        var joint = _joints.GetOrCreateWeldJoint(host, shuttle, shuttle.Comp.JointId);

        joint.LocalAnchorA = Vector2.Transform(anchorWorld, _transform.GetInvWorldMatrix(host));
        joint.LocalAnchorB = Vector2.Zero;
        joint.ReferenceAngle = (float) (_transform.GetWorldRotation(shuttle) - _transform.GetWorldRotation(host));
        joint.CollideConnected = false;
        joint.Stiffness = stiffness;
        joint.Damping = damping;
    }

    private void RemoveWeld(Entity<LandedShuttleComponent> shuttle)
    {
        // Does nothing if the joint is already gone.
        _joints.RemoveJoint(shuttle, shuttle.Comp.JointId);
    }

    private Vector2 GetHostVelocityAt(EntityUid host, Vector2 worldPosition)
    {
        if (!TryComp<PhysicsComponent>(host, out var hostBody))
            return Vector2.Zero;

        var hostXform = Transform(host);
        var localPoint = Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(hostXform));
        return _physics.GetLinearVelocity(host, localPoint, hostBody, hostXform);
    }

    private void MatchHostVelocity(EntityUid shuttle, EntityUid host)
    {
        if (!TryComp<PhysicsComponent>(shuttle, out var shuttleBody))
            return;

        var angular = TryComp<PhysicsComponent>(host, out var hostBody) ? hostBody.AngularVelocity : 0f;

        _physics.SetLinearVelocity(shuttle, GetHostVelocityAt(host, _transform.GetWorldPosition(shuttle)), body: shuttleBody);
        _physics.SetAngularVelocity(shuttle, angular, body: shuttleBody);
    }

    #endregion

    #region While landed

    private void OnFTLAttempt(Entity<LandedShuttleComponent> ent, ref ConsoleFTLAttemptEvent args)
    {
        args.Cancelled = true;
        args.Reason = Loc.GetString("shuttle-console-landed");
    }

    private void OnTileFriction(Entity<LandedShuttleComponent> ent, ref TileFrictionEvent args)
    {
        // The shuttle is cargo now, it should not brake the grid that carries it.
        args.Modifier = 0f;
    }

    private void OnFTLStarted(ref FTLStartedEvent args)
    {
        // Something sent a landed shuttle to FTL without asking the console. Let it go.
        TakeOff(args.Entity);
        CarryLanded(args.Entity);
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        CarryLanded(args.Entity);
    }

    /// <summary>
    /// Brings every shuttle landed on <paramref name="host"/> along after the host has been moved to another place,
    /// and whatever is landed on those shuttles too.
    /// </summary>
    private void CarryLanded(EntityUid host, int depth = 0)
    {
        if (depth > 16)
            return;

        var landedShuttles = new List<Entity<LandedShuttleComponent>>();
        var query = EntityQueryEnumerator<LandedShuttleComponent>();

        while (query.MoveNext(out var uid, out var landed))
        {
            if (landed.Host == host)
                landedShuttles.Add((uid, landed));
        }

        foreach (var shuttle in landedShuttles)
        {
            PlaceOnHost(shuttle);
            CarryLanded(shuttle, depth + 1);
        }
    }

    /// <summary>
    /// Puts the shuttle back where it landed, relative to its host, and welds it again.
    /// </summary>
    private void PlaceOnHost(Entity<LandedShuttleComponent> shuttle)
    {
        var hostXform = Transform(shuttle.Comp.Host);

        if (hostXform.MapUid is not { } mapUid)
        {
            TakeOff(shuttle);
            return;
        }

        RemoveWeld(shuttle);

        var position = Vector2.Transform(shuttle.Comp.LocalPosition, _transform.GetWorldMatrix(hostXform));
        var rotation = _transform.GetWorldRotation(hostXform) + shuttle.Comp.LocalRotation;

        _transform.SetCoordinates(shuttle, Transform(shuttle), new EntityCoordinates(mapUid, position), rotation: rotation);
        MatchHostVelocity(shuttle, shuttle.Comp.Host);
        Weld(shuttle);
    }

    #endregion
}
