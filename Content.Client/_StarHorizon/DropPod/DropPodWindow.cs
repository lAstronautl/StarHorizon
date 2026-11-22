using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Окно консоли дроп-пода. Слева радар сканера масс (центр - консоль, радиус - дальность захвата): клик задаёт
/// точку посадки или захватывает шаттл (в зависимости от режима). Справа состояние капсул, прогресс и кнопки.
/// </summary>
public sealed class DropPodWindow : DefaultWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEntityManager _ent = default!;

    private static readonly Color Accent = Color.FromHex("#ff9a30");
    private static readonly Color Good = Color.FromHex("#5fd35f");
    private static readonly Color Target = Color.FromHex("#ff4040");
    private static readonly Color Capturing = Color.FromHex("#f0c674");

    private enum ClickMode
    {
        Target,
        Capture,
        Planet,
    }

    // Радиус радара задаётся фиксированным: он равен дальности захвата (state.CaptureRange).
    private readonly ShuttleNavControl _radar = new(256f, 256f, 256f)
    {
        HorizontalExpand = true,
        VerticalExpand = true,
        MinSize = new Vector2(480, 480),
        ShowDocks = false,
    };

    private readonly Font _font;

    private readonly Label _status = new() { HorizontalExpand = true, FontColorOverride = Accent };
    private readonly Label _occupants = new();
    private readonly ProgressBar _podProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _timer = new();
    private readonly Label _error = new() { FontColorOverride = Color.OrangeRed, HorizontalExpand = true };

    private readonly Button _modeTarget = new() { ToggleMode = true, HorizontalExpand = true };
    private readonly Button _modeCapture = new() { ToggleMode = true, HorizontalExpand = true };
    private readonly Button _modePlanet = new() { ToggleMode = true, HorizontalExpand = true };

    // Планеты с экспедициями: выбор цели списком (планеты далеко за радиусом радара).
    private readonly BoxContainer _planetBox = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Visible = false };
    private readonly BoxContainer _planetRows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly Button _launchPlanet = new();
    private readonly List<(Label Status, PlanetExpeditionInfo Info)> _planetStatuses = new();
    private NetEntity? _selectedPlanet;
    private string _planetSignature = string.Empty;
    private readonly Label _targetLabel = new();
    private readonly Button _launch = new();
    private readonly Button _abort = new();

    private readonly Button _release = new();
    private readonly ProgressBar _captureProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _captureStatus = new() { HorizontalExpand = true };
    private readonly Button _launchCaptured = new();
    private readonly Label _armed = new() { HorizontalExpand = true, FontColorOverride = Good };

    private DropPodConsoleState? _state;
    private ClickMode _mode = ClickMode.Target;
    private Vector2? _target;
    private EntityUid? _console;
    private bool _initialized;
    private readonly SharedTransformSystem _xform;

    public event Action<Vector2>? OnArm;
    public event Action<Vector2>? OnLaunch;
    public event Action? OnLaunchToCaptured;
    public event Action? OnAbort;
    public event Action<NetEntity>? OnCapture;
    public event Action<NetEntity>? OnArmPlanet;
    public event Action<NetEntity>? OnLaunchPlanet;
    public event Action? OnRelease;

    public DropPodWindow()
    {
        IoCManager.InjectDependencies(this);
        _xform = _ent.System<SharedTransformSystem>();

        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

        Title = Loc.GetString("droppod-ui-title");
        MinSize = new Vector2(900, 620);

        _modeTarget.Text = Loc.GetString("droppod-ui-mode-target");
        _modeCapture.Text = Loc.GetString("droppod-ui-mode-capture");
        _modePlanet.Text = Loc.GetString("droppod-ui-mode-planet");
        _launchPlanet.Text = Loc.GetString("droppod-ui-launch-planet");
        _launch.Text = Loc.GetString("droppod-ui-launch");
        _launchCaptured.Text = Loc.GetString("droppod-ui-launch-captured");
        _abort.Text = Loc.GetString("droppod-ui-abort");
        _release.Text = Loc.GetString("droppod-ui-release");

        var group = new ButtonGroup(false);
        _modeTarget.Group = group;
        _modeCapture.Group = group;
        _modePlanet.Group = group;
        _modeTarget.Pressed = true;

        // Радар сканера масс: центрирован на консоли, клик выбирает точку или шаттл.
        _radar.OnRadarClick += OnRadarClicked;
        _radar.DrawOverlay += DrawOverlay;

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 320,
        };

        side.AddChild(Header(Loc.GetString("droppod-ui-section-pod")));
        side.AddChild(_status);
        side.AddChild(_occupants);
        side.AddChild(_podProgress);
        side.AddChild(_timer);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-click")));
        var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        modes.AddChild(_modeTarget);
        modes.AddChild(_modeCapture);
        modes.AddChild(_modePlanet);
        side.AddChild(modes);

        _planetBox.AddChild(Header(Loc.GetString("droppod-ui-section-planets")));
        _planetBox.AddChild(_planetRows);
        _launchPlanet.ModulateSelfOverride = Good;
        _planetBox.AddChild(_launchPlanet);
        side.AddChild(_planetBox);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-coords")));
        side.AddChild(_targetLabel);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _launch.HorizontalExpand = true;
        buttons.AddChild(_launch);
        side.AddChild(buttons);
        _abort.HorizontalExpand = true;
        side.AddChild(_abort);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-capture")));
        side.AddChild(_captureStatus);
        side.AddChild(_captureProgress);
        _release.HorizontalExpand = true;
        side.AddChild(_release);
        _launchCaptured.ModulateSelfOverride = Good;
        side.AddChild(_launchCaptured);
        side.AddChild(_armed);
        side.AddChild(_error);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        root.AddChild(_radar);
        // У ScrollContainer нет собственной минимальной ширины: без неё радар забирает всё место и панель пропадает.
        root.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, MinWidth = 340, Children = { side } });
        Contents.AddChild(root);

        _modeTarget.OnPressed += _ => SetMode(ClickMode.Target);
        _modeCapture.OnPressed += _ => SetMode(ClickMode.Capture);
        _modePlanet.OnPressed += _ => SetMode(ClickMode.Planet);
        _launchPlanet.OnPressed += _ =>
        {
            if (_selectedPlanet is { } planet)
                OnLaunchPlanet?.Invoke(planet);
        };

        _launch.OnPressed += _ =>
        {
            if (_target is { } t)
                OnLaunch?.Invoke(t);
        };
        _launchCaptured.OnPressed += _ => OnLaunchToCaptured?.Invoke();
        _abort.OnPressed += _ => OnAbort?.Invoke();
        _release.OnPressed += _ => OnRelease?.Invoke();
    }

    private static Control Header(string text)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 6, 0, 0) };
        box.AddChild(new Label { Text = text, FontColorOverride = Accent });
        box.AddChild(new PanelContainer { MinHeight = 2, PanelOverride = new StyleBoxFlat { BackgroundColor = Accent } });
        return box;
    }

    private void SetMode(ClickMode mode)
    {
        _mode = mode;
        _planetBox.Visible = mode == ClickMode.Planet;
    }

    #region Planets

    /// <summary>Перестраивает список планет, если он изменился. Выбранная планета остаётся выбранной.</summary>
    private void RefreshPlanets(List<PlanetExpeditionInfo> planets)
    {
        var signature = string.Join(";", planets.Select(p => $"{p.Planet}:{p.Status}:{p.Mission.Index}:{p.EndTime.Ticks}"));
        if (signature == _planetSignature)
            return;

        _planetSignature = signature;
        _planetRows.DisposeAllChildren();
        _planetStatuses.Clear();

        var group = new ButtonGroup();
        foreach (var info in planets)
        {
            var type = Loc.GetString($"salvage-expedition-type-{info.Mission.MissionType}");
            var difficulty = Loc.GetString($"salvage-expedition-difficulty-{info.Mission.Difficulty}");

            var button = new Button
            {
                Text = info.Name,
                ToggleMode = true,
                Group = group,
                HorizontalExpand = true,
                ClipText = true,
            };
            var missionText = $"{type}, {difficulty}";
            button.Pressed = _selectedPlanet == info.Planet;
            button.OnPressed += _ =>
            {
                _selectedPlanet = info.Planet;
                UpdateButtons();
                OnArmPlanet?.Invoke(info.Planet);
            };

            var mission = new Label { Text = missionText, Margin = new Thickness(8, 0, 0, 0), ClipText = true };
            var status = new Label { Margin = new Thickness(8, 0, 0, 2), ClipText = true };

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            row.AddChild(button);
            row.AddChild(mission);
            row.AddChild(status);
            _planetRows.AddChild(row);
            _planetStatuses.Add((status, info));
        }

        UpdatePlanetStatuses();
        UpdateButtons();
    }

    private void UpdatePlanetStatuses()
    {
        foreach (var (label, info) in _planetStatuses)
        {
            switch (info.Status)
            {
                case PlanetExpeditionStatus.Idle:
                    var idleLeft = info.EndTime - _timing.CurTime;
                    if (idleLeft < TimeSpan.Zero)
                        idleLeft = TimeSpan.Zero;
                    label.Text = Loc.GetString("salvage-expedition-planet-status-idle",
                        ("time", $"{(int) idleLeft.TotalMinutes:00}:{idleLeft.Seconds:00}"));
                    label.FontColorOverride = Color.Gray;
                    break;
                case PlanetExpeditionStatus.Generating:
                    label.Text = Loc.GetString("salvage-expedition-planet-status-generating");
                    label.FontColorOverride = Capturing;
                    break;
                default:
                    var remaining = info.EndTime - _timing.CurTime;
                    label.Text = remaining <= TimeSpan.Zero
                        ? Loc.GetString("salvage-expedition-planet-status-closing")
                        : Loc.GetString("salvage-expedition-planet-status-active",
                            ("time", $"{(int) remaining.TotalMinutes:00}:{remaining.Seconds:00}"));
                    label.FontColorOverride = Good;
                    break;
            }
        }
    }

    #endregion

    #region Radar

    /// <summary>Живая позиция консоли (радар центрирован на ней и следует за её шаттлом).</summary>
    private Vector2 ConsoleWorld(DropPodConsoleState state)
    {
        return _console is { } console && _ent.EntityExists(console)
            ? _xform.GetWorldPosition(console)
            : state.ConsolePosition;
    }

    /// <summary>Живая позиция шаттла: если клиент знает сетку - центр её корпуса, иначе позиция из состояния.</summary>
    private Vector2 GridWorld(DropPodGridInfo info)
    {
        if (_ent.TryGetEntity(info.Grid, out var uid) && _ent.TryGetComponent(uid, out MapGridComponent? grid))
            return Vector2.Transform(grid.LocalAABB.Center, _xform.GetWorldMatrix(uid.Value));

        return info.Position;
    }

    private void OnRadarClicked(EntityCoordinates coords)
    {
        if (_mode == ClickMode.Planet || _state is not { } state || !coords.IsValid(_ent))
            return;

        var world = _xform.ToMapCoordinates(coords).Position;

        // Радар показывает область в радиусе захвата; клики в углах квадратного контрола за её пределами не принимаем.
        if ((world - ConsoleWorld(state)).Length() > state.CaptureRange)
        {
            _error.Text = Loc.GetString("droppod-ui-out-of-range");
            return;
        }

        if (_mode == ClickMode.Target)
        {
            // Выбор точки сразу задаёт цель привязанным капсулам: дальше они ждут триггера любого устройства.
            _target = world;
            UpdateTargetLabel();
            UpdateButtons();
            OnArm?.Invoke(world);
            return;
        }

        // Режим захвата: ближайший шаттл под курсором (в радиусе корпуса + небольшой запас).
        DropPodGridInfo? best = null;
        var bestDist = float.MaxValue;
        foreach (var info in state.Grids)
        {
            var dist = (GridWorld(info) - world).Length();
            if (dist > info.Radius + 15f || dist >= bestDist)
                continue;

            best = info;
            bestDist = dist;
        }

        if (best is { } picked)
            OnCapture?.Invoke(picked.Grid);
        else
            _error.Text = Loc.GetString("droppod-ui-no-shuttle-there");
    }

    /// <summary>
    /// Поверх радара: захват шаттла, выбранная точка посадки и цель летящей капсулы. Круг дальности и метка консоли
    /// уже есть на самом радаре (его радиус равен дальности захвата).
    /// </summary>
    private void DrawOverlay(DrawingHandleScreen handle, Func<Vector2, Vector2> toControl)
    {
        if (_state is not { } state)
            return;

        var console = toControl(ConsoleWorld(state));

        // Захват / идущий захват.
        foreach (var info in state.Grids)
        {
            Color? color = null;
            if (state.CapturedGrid == info.Grid)
                color = Good;
            else if (state.CapturingGrid == info.Grid)
                color = Capturing;

            if (color is not { } c)
                continue;

            var world = GridWorld(info);
            var pos = toControl(world);
            var radius = MathF.Max(12f, (toControl(world + new Vector2(info.Radius, 0f)) - pos).Length() + 6f);
            handle.DrawCircle(pos, radius, c.WithAlpha(0.1f), true);
            handle.DrawCircle(pos, radius, c, false);
            handle.DrawLine(console, pos, c.WithAlpha(0.6f));
        }

        // Выбранная точка посадки.
        if (_target is { } target)
            DrawTargetMarker(handle, toControl(target), Target, $"{target.X:0}, {target.Y:0}");

        // Куда уже летит капсула.
        if (state.Target is { } flying)
            DrawTargetMarker(handle, toControl(flying), Accent, Loc.GetString("droppod-ui-in-flight"));
    }

    private void DrawTargetMarker(DrawingHandleScreen handle, Vector2 pos, Color color, string label)
    {
        handle.DrawCircle(pos, 9f, color.WithAlpha(0.12f), true);
        handle.DrawCircle(pos, 9f, color, false);
        handle.DrawLine(pos + new Vector2(-14f, 0f), pos + new Vector2(-4f, 0f), color);
        handle.DrawLine(pos + new Vector2(4f, 0f), pos + new Vector2(14f, 0f), color);
        handle.DrawLine(pos + new Vector2(0f, -14f), pos + new Vector2(0f, -4f), color);
        handle.DrawLine(pos + new Vector2(0f, 4f), pos + new Vector2(0f, 14f), color);
        DrawLabel(handle, label, pos + new Vector2(12f, 8f), color);
    }

    private void DrawLabel(DrawingHandleScreen handle, string text, Vector2 pos, Color color)
    {
        handle.DrawString(_font, pos + new Vector2(1f, 1f), text, Color.Black.WithAlpha(0.7f));
        handle.DrawString(_font, pos, text, color);
    }

    /// <summary>Передаёт радару консоль как центр и дальность захвата как радиус.</summary>
    private void UpdateRadar(DropPodConsoleState state)
    {
        if (_console is not { } console || !_ent.TryGetComponent(console, out TransformComponent? xform))
            return;

        _radar.SetConsole(console);
        _radar.UpdateState(new NavInterfaceState(
            state.CaptureRange,
            _ent.GetNetCoordinates(xform.Coordinates),
            Angle.Zero,
            new Dictionary<NetEntity, List<DockingPortState>>(),
            InertiaDampeningMode.Dampen,
            ServiceFlags.None,
            null,
            null,
            false));
    }

    public void SetConsole(EntityUid console)
    {
        _console = console;
    }

    #endregion

    private void UpdateTargetLabel()
    {
        _targetLabel.Text = _target is { } t
            ? Loc.GetString("droppod-ui-target-selected", ("x", t.X.ToString("0", CultureInfo.InvariantCulture)), ("y", t.Y.ToString("0", CultureInfo.InvariantCulture)))
            : Loc.GetString("droppod-ui-target-none");
    }

    private void UpdateButtons()
    {
        if (_state is not { } state)
            return;

        var idle = state.Linked && state.ReadyCount > 0 && state.PodState == DropPodState.Idle;
        var captured = state.CapturedGrid != null;
        var capturing = state.CapturingGrid != null;

        _launch.Disabled = !idle || _target == null;
        _launchCaptured.Disabled = !idle || !captured;
        _armed.Text = state.ArmedCount > 0 ? Loc.GetString("droppod-ui-armed", ("count", state.ArmedCount)) : string.Empty;
        _launchPlanet.Disabled = !idle || _selectedPlanet == null;
        _abort.Disabled = !(state.Linked && state.PodState == DropPodState.Countdown);
        _release.Disabled = !captured && !capturing;
    }

    public void UpdateState(DropPodConsoleState state)
    {
        _state = state;

        if (!_initialized)
        {
            _initialized = true;
            UpdateTargetLabel();
        }

        UpdateRadar(state);
        RefreshPlanets(state.Planets);

        _error.Text = state.Error ?? string.Empty;

        if (!state.Linked)
        {
            _status.Text = Loc.GetString("droppod-ui-no-pod");
            _occupants.Text = string.Empty;
        }
        else
        {
            _status.Text = state.ReadyCount == 0 && state.PodState == DropPodState.Idle
                ? Loc.GetString("droppod-ui-used")
                : Loc.GetString("droppod-ui-state", ("state", Loc.GetString($"droppod-state-{state.PodState}")))
                  + "\n" + Loc.GetString("droppod-ui-pods", ("count", state.PodCount), ("ready", state.ReadyCount));
            _occupants.Text = Loc.GetString("droppod-ui-occupants", ("count", state.Occupants));
        }

        if (state.CapturedGrid is { } held)
        {
            _captureStatus.FontColorOverride = Good;
            _captureStatus.Text = Loc.GetString("droppod-ui-captured", ("name", GridName(state, held)));
        }
        else if (state.CapturingGrid is { } capturing)
        {
            _captureStatus.FontColorOverride = Capturing;
            _captureStatus.Text = Loc.GetString("droppod-ui-capturing", ("name", GridName(state, capturing)));
        }
        else
        {
            _captureStatus.FontColorOverride = null;
            _captureStatus.Text = Loc.GetString("droppod-ui-not-captured", ("range", (int) state.CaptureRange));
        }

        UpdateButtons();
    }

    private static string GridName(DropPodConsoleState state, NetEntity grid)
    {
        foreach (var info in state.Grids)
        {
            if (info.Grid == grid)
                return info.Name;
        }

        return "?";
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_state is not { } state)
            return;

        UpdatePlanetStatuses();

        // Прогресс капсулы.
        if (state.Linked && state.PodState != DropPodState.Idle)
        {
            var total = (state.StateEnd - state.StateStart).TotalSeconds;
            var left = Math.Max(0, (state.StateEnd - _timing.CurTime).TotalSeconds);
            _podProgress.Value = total <= 0 ? 1f : (float) Math.Clamp(1 - left / total, 0, 1);
            _timer.Text = Loc.GetString("droppod-ui-timer", ("seconds", left.ToString("0.0", CultureInfo.InvariantCulture)));
        }
        else
        {
            _podProgress.Value = 0f;
            _timer.Text = string.Empty;
        }

        // Прогресс захвата.
        if (state.CapturingGrid != null)
        {
            var total = state.CaptureTime.TotalSeconds;
            var left = Math.Max(0, (state.CaptureEnd - _timing.CurTime).TotalSeconds);
            _captureProgress.Value = total <= 0 ? 1f : (float) Math.Clamp(1 - left / total, 0, 1);
        }
        else
        {
            _captureProgress.Value = state.CapturedGrid != null ? 1f : 0f;
        }
    }
}
