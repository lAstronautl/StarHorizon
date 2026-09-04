using System.Linq;
using System.Numerics;
using Content.Client.GameTicking.Managers;
using Content.Client.Pointing.Components;
using Content.Shared._Mono.Saiga;
using Content.Shared.CCVar;
using Content.Shared.Construction;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Mono.SaigaAgent;

/// <summary>
///     Client-side AI agent that pilots the local player's character. Enabling sends one event to
///     the server brain, which then reacts (event-driven) whenever the character hears someone and
///     pushes back a decision. This client talks via the "say" command and walks by holding
///     movement keys ("incmd") toward a follow target.
///
///     IMPORTANT: input commands (incmd) are only issued from <see cref="FrameUpdate"/> — never
///     from network handlers — because those run inside the sim tick / prediction, and injecting
///     input there mutates the input queue mid-enumeration (crash).
/// </summary>
public sealed class SaigaAgentSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _con = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ClientGameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly InputSystem _inputSys = default!;

    private TimeSpan _nextAutoStart;

    public bool Enabled { get; private set; }
    public string Goal { get; private set; } =
        "Веди себя как обычный член экипажа: отвечай, когда к тебе обращаются, и иди за тем, кто просит.";

    // Steering (state only; actual key presses happen in FrameUpdate).
    private NetEntity? _followTarget;          // follow a moving entity
    private MapCoordinates? _gotoTarget;       // walk to a fixed point (e.g. a pointed location)
    private NetEntity? _pickupTarget;          // walk to an item, then grab it
    private NetEntity? _pullTarget;            // walk to an item, then start pulling it
    private NetEntity? _moveToTarget;          // walk right up to an entity (no interaction)
    private NetEntity? _placeTarget;           // walk right up to an entity, then drop the held item
    private readonly Queue<(BoundKeyFunction Func, NetEntity? Target, bool AtCoords)> _pending = new();

    // Build a wall: after "построй стену", the next point sets the build tile; she walks there and
    // raises the girder construction (consuming steel from her hand). Stage 1.
    private bool _awaitBuildPoint;
    private EntityCoordinates? _buildCoords;
    private int _buildAck;
    private const float BuildRange = 1.4f;
    private const string GirderPrototype = "Girder";

    private const float StopRange = 1.8f;
    private const float PickupApproach = 1.0f; // get this close before trying to grab
    private const float PickupRange = 1.6f;    // try the grab once within this distance
    private const float MoveToRange = 0.4f;    // arrive this close for move_to / place (right on the spot)
    // Per-axis threshold to press a movement key. MUST stay below the tightest arrival (MoveToRange),
    // else the agent stops correcting an axis while still short of the target and walks past it.
    private const float Deadzone = 0.15f;
    private const float PointDetectRange = 12f; // how close a fresh pointing-arrow must be to react
    private readonly HashSet<string> _heldKeys = new();
    private readonly HashSet<EntityUid> _seenArrows = new();

    // Local obstacle avoidance (slide along one axis when stuck).
    private TimeSpan _stuckCheck;
    private float _prevDist;
    private int _slideMode; // 0 = straight to target, 1 = X axis only, 2 = Y axis only

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(SaigaCVars.AgentGoal, v => Goal = v, true);
        SubscribeNetworkEvent<SaigaAgentDecisionResponseEvent>(OnDecision);
    }

    /// <summary>Autostart for headless: once connected, join the round and enable the agent.</summary>
    public override void Update(float frameTime)
    {
        if (!_cfg.GetCVar(SaigaCVars.AgentAutoStart))
            return;
        if (_timing.CurTime < _nextAutoStart)
            return;
        _nextAutoStart = _timing.CurTime + TimeSpan.FromSeconds(3);

        if (_player.LocalSession == null)
            return; // not connected yet

        if (!Enabled)
        {
            SetEnabled(true); // enables + first join attempt
            return;
        }

        // Keep trying to enter the round until we actually have a body.
        var inGame = _player.LocalEntity is { } self && !Deleted(self);
        if (!inGame)
            TryJoin();
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            _followTarget = null; // keys released next FrameUpdate
            _gotoTarget = null;
        }
        RaiseNetworkEvent(new SaigaAgentEnableEvent(
            enabled,
            Goal,
            _cfg.GetCVar(SaigaCVars.AgentEndpoint),
            _cfg.GetCVar(SaigaCVars.AgentNumGpu)));

        // Headless convenience: if we're not in the round yet, join automatically.
        if (enabled)
        {
            var inGame = _player.LocalEntity is { } self && !Deleted(self);
            if (!inGame)
                TryJoin();
        }
    }

    /// <summary>
    ///     Joins the round without UI (for headless): readies up in lobby, or late-joins as a
    ///     passenger if the round is already running. Returns a human-readable status.
    /// </summary>
    public string TryJoin()
    {
        if (_player.LocalEntity is { } ent && !Deleted(ent))
            return "уже в игре";

        if (!_ticker.IsGameStarted)
        {
            _con.ExecuteCommand("toggleready true");
            return "готов в лобби — заспавнюсь при старте раунда";
        }

        if (_ticker.DisallowedLateJoin)
            return "поздний вход запрещён сервером";

        if (_ticker.StationNames.Count == 0)
            return "нет доступных станций";

        var station = _ticker.StationNames.Keys.First();
        _con.ExecuteCommand($"joingame Passenger {station}");
        return "захожу в раунд (Passenger)";
    }

    public void SetGoal(string goal)
    {
        Goal = goal;
        if (Enabled)
            RaiseNetworkEvent(new SaigaAgentEnableEvent(
                true,
                Goal,
                _cfg.GetCVar(SaigaCVars.AgentEndpoint),
                _cfg.GetCVar(SaigaCVars.AgentNumGpu)));
    }

    /// <summary>Runs between ticks — the only safe place to issue input (incmd / interactions).</summary>
    public override void FrameUpdate(float frameTime)
    {
        ExecutePending();
        TryReachInteract(ref _pickupTarget, EngineKeyFunctions.Use);          // pick up
        TryReachInteract(ref _pullTarget, ContentKeyFunctions.TryPullObject); // start pulling
        TryReachPlace();                                                      // walk up, then drop
        TryBuild();
        DetectPointingArrow();
        ApplyKeys(DesiredKeys());
    }

    /// <summary>When at the build tile, raise the girder construction (server consumes held steel).</summary>
    private void TryBuild()
    {
        if (_buildCoords is not { } coords)
            return;

        if (_player.LocalEntity is not { } self || Deleted(self))
        {
            _buildCoords = null;
            return;
        }

        var selfPos = _xform.GetMapCoordinates(self);
        var buildPos = _xform.ToMapCoordinates(coords);
        if (selfPos.MapId != buildPos.MapId)
            return;

        if ((buildPos.Position - selfPos.Position).Length() > BuildRange)
            return; // keep walking toward the tile

        RaiseNetworkEvent(new TryStartStructureConstructionMessage(
            GetNetCoordinates(coords), GirderPrototype, Angle.Zero, unchecked(_buildAck++)));
        _buildCoords = null;
    }

    /// <summary>Walk-to-then-interact: once within reach of the target entity, fire the interaction.</summary>
    private void TryReachInteract(ref NetEntity? target, BoundKeyFunction func)
    {
        if (target is not { } net)
            return;

        if (_player.LocalEntity is not { } self || Deleted(self)
            || !TryGetEntity(net, out var ent) || Deleted(ent.Value))
        {
            target = null;
            return;
        }

        var selfPos = _xform.GetMapCoordinates(self);
        var entPos = _xform.GetMapCoordinates(ent.Value);
        if (selfPos.MapId != entPos.MapId)
            return;

        if ((entPos.Position - selfPos.Position).Length() > PickupRange)
            return; // keep walking toward it

        SendInput(func, Transform(ent.Value).Coordinates, ent.Value);
        target = null;
    }

    /// <summary>Walk-to-then-drop: once right next to the place target, drop the held item there.</summary>
    private void TryReachPlace()
    {
        if (_placeTarget is not { } net)
            return;

        if (_player.LocalEntity is not { } self || Deleted(self)
            || !TryGetEntity(net, out var ent) || Deleted(ent.Value))
        {
            _placeTarget = null;
            return;
        }

        var selfPos = _xform.GetMapCoordinates(self);
        var entPos = _xform.GetMapCoordinates(ent.Value);
        if (selfPos.MapId != entPos.MapId)
            return;

        if ((entPos.Position - selfPos.Position).Length() > MoveToRange + 0.3f)
            return; // keep walking toward it

        _pending.Enqueue((ContentKeyFunctions.Drop, null, false));
        _placeTarget = null;
    }

    /// <summary>Runs instant actions (drop, swap, throw, store) queued from the network handler.</summary>
    private void ExecutePending()
    {
        while (_pending.Count > 0)
        {
            var (func, target, atCoords) = _pending.Dequeue();
            if (_player.LocalEntity is not { } self || Deleted(self))
                continue;

            if (target is { } net)
            {
                if (!TryGetEntity(net, out var ent) || Deleted(ent.Value))
                    continue;
                // atCoords = aim at the target's position (throw); else interact ON the entity (store).
                SendInput(func, Transform(ent.Value).Coordinates, atCoords ? EntityUid.Invalid : ent.Value);
            }
            else
            {
                SendInput(func, Transform(self).Coordinates, EntityUid.Invalid);
            }
        }
    }

    /// <summary>Sends a bound-key input command (like a click/keypress) — optionally at a target entity.</summary>
    private void SendInput(BoundKeyFunction func, EntityCoordinates coords, EntityUid uid)
    {
        if (_player.LocalSession is not { } session)
            return;

        var funcId = _input.NetworkBindMap.KeyFunctionID(func);

        var down = new ClientFullInputCmdMessage(_timing.CurTick, _timing.TickFraction, funcId,
            coords, new ScreenCoordinates(0, 0, default), BoundKeyState.Down, uid);
        _inputSys.HandleInputCommand(session, func, down);

        var up = new ClientFullInputCmdMessage(_timing.CurTick, _timing.TickFraction, funcId,
            coords, new ScreenCoordinates(0, 0, default), BoundKeyState.Up, uid);
        _inputSys.HandleInputCommand(session, func, up);
    }

    /// <summary>
    ///     Watches for a fresh "point" arrow nearby (any player pointing) and locks its location
    ///     as a one-shot go-to target. Arrows spawn exactly at the pointed coordinates and are
    ///     networked, so we just read the nearest new one from our PVS bubble.
    /// </summary>
    private void DetectPointingArrow()
    {
        if (!Enabled || _player.LocalEntity is not { } self || Deleted(self))
            return;

        var selfPos = _xform.GetMapCoordinates(self);
        var current = new HashSet<EntityUid>();
        EntityUid? nearest = null;
        var nearestDist = float.MaxValue;
        var nearestPos = MapCoordinates.Nullspace;

        var query = EntityQueryEnumerator<PointingArrowComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            current.Add(uid);
            if (_seenArrows.Contains(uid))
                continue; // already handled this arrow

            var pos = _xform.GetMapCoordinates(uid);
            if (pos.MapId != selfPos.MapId)
                continue;

            var dist = (pos.Position - selfPos.Position).Length();
            if (dist <= PointDetectRange && dist < nearestDist)
            {
                nearestDist = dist;
                nearest = uid;
                nearestPos = pos;
            }
        }

        // Remember all current arrows so we don't re-trigger; drop ones that vanished.
        _seenArrows.UnionWith(current);
        _seenArrows.IntersectWith(current);

        if (nearest == null)
            return;

        if (_awaitBuildPoint)
        {
            _buildCoords = Transform(nearest.Value).Coordinates; // build tile
            _awaitBuildPoint = false;
        }
        else
        {
            _gotoTarget = nearestPos;
        }
    }

    private void OnDecision(SaigaAgentDecisionResponseEvent ev)
    {
        if (!Enabled)
            return;

        if (!string.IsNullOrWhiteSpace(ev.Say))
        {
            var text = Sanitize(ev.Say!);
            if (text.Length > 0)
                _con.ExecuteCommand($"say {text}"); // "say" is not an input command — safe here
        }

        switch (ev.Action?.Trim().ToLowerInvariant())
        {
            case "follow":
                ClearMovement();
                _followTarget = ev.Target;
                break;
            case "pickup":
                ClearMovement();
                _pickupTarget = ev.Target;
                break;
            case "move_to":  // walk right up to the target, no interaction
                ClearMovement();
                _moveToTarget = ev.Target;
                break;
            case "place":  // walk right up to the target, then drop the held item
                ClearMovement();
                _placeTarget = ev.Target;
                break;
            case "pull":
                ClearMovement();
                _pullTarget = ev.Target;
                break;
            case "store":  // use held item on the backpack
                _pending.Enqueue((EngineKeyFunctions.Use, ev.Target, false));
                break;
            case "throw":  // throw held item toward the speaker's position
                _pending.Enqueue((ContentKeyFunctions.ThrowItemInHand, ev.Target, true));
                break;
            case "drop":
                _pending.Enqueue((ContentKeyFunctions.Drop, null, false));
                break;
            case "swap":
                _pending.Enqueue((ContentKeyFunctions.SwapHands, null, false));
                break;
            case "build":  // next point sets the build tile
                ClearMovement();
                _awaitBuildPoint = true;
                break;
            case "craft":  // craft an item recipe from materials in hand / nearby
                if (!string.IsNullOrWhiteSpace(ev.Arg))
                    RaiseNetworkEvent(new TryStartItemConstructionMessage(ev.Arg!));
                break;
            case "construct":  // place a structure / machine frame recipe at the agent's tile
                if (!string.IsNullOrWhiteSpace(ev.Arg)
                    && _player.LocalEntity is { } cself && !Deleted(cself))
                    RaiseNetworkEvent(new TryStartStructureConstructionMessage(
                        GetNetCoordinates(Transform(cself).Coordinates), ev.Arg!, Angle.Zero, unchecked(_buildAck++)));
                break;
            case "use_on":  // use the held item on the target (insert board/part into a frame, etc.)
                _pending.Enqueue((EngineKeyFunctions.Use, ev.Target, false));
                break;
            case "activate":  // toggle the held item in hand (light a welder, flashlight, ...)
                _pending.Enqueue((ContentKeyFunctions.UseItemInHand, null, false));
                break;
            case "stop":
                _followTarget = null;
                _pickupTarget = null;
                _pullTarget = null;
                _gotoTarget = null;
                _moveToTarget = null;
                _placeTarget = null;
                _buildCoords = null;
                _awaitBuildPoint = false;
                break;
        }
    }

    /// <summary>Clears every walk intent — called before setting a fresh one so they don't pin each other.</summary>
    private void ClearMovement()
    {
        _followTarget = null;
        _pickupTarget = null;
        _pullTarget = null;
        _moveToTarget = null;
        _placeTarget = null;
        _gotoTarget = null;
    }

    private HashSet<string> DesiredKeys()
    {
        var want = new HashSet<string>();

        if (!Enabled || _player.LocalEntity is not { } self || Deleted(self))
            return want;

        if (!TryResolveTargetPos(self, out var targetPos, out var arrival, out var isGoto))
            return want;

        var selfPos = _xform.GetMapCoordinates(self);
        if (selfPos.MapId != targetPos.MapId)
            return want;

        var delta = targetPos.Position - selfPos.Position;
        var dist = delta.Length();
        if (dist <= arrival)
        {
            if (isGoto)
            {
                _gotoTarget = null;   // arrived at the fixed point
                _moveToTarget = null; // move_to is one-shot — release on arrival
            }
            return want;
        }

        // Movement keys are interpreted relative to the grid/eye rotation (engine rotates input by
        // GetParentGridAngle). Convert the world direction into that frame, else she walks wrong.
        if (_cfg.GetCVar(CCVars.RelativeMovement) && TryComp<InputMoverComponent>(self, out var mover))
        {
            var parentRotation = mover.RelativeRotation;
            if (mover.RelativeEntity is { } rel && !Deleted(rel))
                parentRotation = _xform.GetWorldRotation(rel) + mover.RelativeRotation;

            delta = (-parentRotation).RotateVec(delta);
        }

        delta = Avoid(delta, dist);
        return KeysFromDelta(delta);
    }

    /// <summary>
    ///     Local obstacle avoidance via axis-sliding: if we stop making progress, walk along just
    ///     one axis toward the target (slide along the wall) instead of pushing into it diagonally.
    ///     Cycles straight -> X-only -> Y-only until progress resumes.
    /// </summary>
    private Vector2 Avoid(Vector2 delta, float dist)
    {
        if (_timing.CurTime >= _stuckCheck)
        {
            _stuckCheck = _timing.CurTime + TimeSpan.FromSeconds(0.35);

            var progressed = _prevDist > 0f && _prevDist - dist > 0.08f;
            if (progressed)
                _slideMode = 0;                 // moving toward target — go straight
            else
                _slideMode = (_slideMode + 1) % 3; // stuck — try the next axis

            _prevDist = dist;
        }

        return _slideMode switch
        {
            1 => new Vector2(delta.X, 0f), // slide along X
            2 => new Vector2(0f, delta.Y), // slide along Y
            _ => delta,                    // straight to target
        };
    }

    private HashSet<string> KeysFromDelta(Vector2 delta)
    {
        var want = new HashSet<string>();
        if (delta.X > Deadzone) want.Add("MoveRight");
        else if (delta.X < -Deadzone) want.Add("MoveLeft");
        if (delta.Y > Deadzone) want.Add("MoveUp");
        else if (delta.Y < -Deadzone) want.Add("MoveDown");
        return want;
    }

    /// <summary>Resolves where to walk: pickup target &gt; pointed point &gt; follow target.</summary>
    private bool TryResolveTargetPos(EntityUid self, out MapCoordinates targetPos, out float arrival, out bool isGoto)
    {
        isGoto = false;
        arrival = StopRange;

        if (_pickupTarget is { } pnet)
        {
            if (TryGetEntity(pnet, out var pent) && !Deleted(pent.Value))
            {
                targetPos = _xform.GetMapCoordinates(pent.Value);
                arrival = PickupApproach;
                return true;
            }
            _pickupTarget = null;
        }

        if (_pullTarget is { } qnet)
        {
            if (TryGetEntity(qnet, out var qent) && !Deleted(qent.Value))
            {
                targetPos = _xform.GetMapCoordinates(qent.Value);
                arrival = PickupApproach;
                return true;
            }
            _pullTarget = null;
        }

        if (_moveToTarget is { } mnet)
        {
            if (TryGetEntity(mnet, out var ment) && !Deleted(ment.Value))
            {
                targetPos = _xform.GetMapCoordinates(ment.Value);
                arrival = MoveToRange;
                isGoto = true; // one-shot: release _moveToTarget on arrival
                return true;
            }
            _moveToTarget = null;
        }

        if (_placeTarget is { } plnet)
        {
            if (TryGetEntity(plnet, out var plent) && !Deleted(plent.Value))
            {
                targetPos = _xform.GetMapCoordinates(plent.Value);
                arrival = MoveToRange;
                return true;
            }
            _placeTarget = null;
        }

        if (_buildCoords is { } bcoords)
        {
            targetPos = _xform.ToMapCoordinates(bcoords);
            arrival = BuildRange;
            return true;
        }

        if (_gotoTarget is { } point)
        {
            targetPos = point;
            isGoto = true;
            return true;
        }

        if (_followTarget is { } net && TryGetEntity(net, out var ent) && !Deleted(ent.Value))
        {
            targetPos = _xform.GetMapCoordinates(ent.Value);
            return true;
        }

        _followTarget = null;
        targetPos = MapCoordinates.Nullspace;
        return false;
    }

    private void ApplyKeys(HashSet<string> want)
    {
        foreach (var key in _heldKeys.ToArray())
        {
            if (!want.Contains(key))
            {
                _con.ExecuteCommand($"incmd {key} Up");
                _heldKeys.Remove(key);
            }
        }
        foreach (var key in want)
        {
            if (_heldKeys.Add(key))
                _con.ExecuteCommand($"incmd {key} Down");
        }
    }

    private static string Sanitize(string s)
    {
        return s.Replace("\r", " ").Replace("\n", " ").Replace("\"", "").Trim();
    }
}
