using System.Globalization;
using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>
/// Окно консоли дроп-пода. Слева карта, как в консоли управления шаттлом: клик задаёт точку посадки
/// или захватывает шаттл (в зависимости от режима). Справа состояние капсул, прогресс и кнопки.
/// </summary>
public sealed class DropPodWindow : DefaultWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _ent = default!;

    private static readonly Color Accent = Color.FromHex("#ff9a30");
    private static readonly Color Good = Color.FromHex("#5fd35f");
    private static readonly Color Target = Color.FromHex("#ff4040");
    private static readonly Color Capturing = Color.FromHex("#f0c674");

    private enum ClickMode
    {
        Target,
        Capture,
    }

    private readonly ShuttleMapControl _map = new()
    {
        HorizontalExpand = true,
        VerticalExpand = true,
        MinSize = new Vector2(480, 480),
    };

    private readonly Font _font;

    private readonly Label _status = new() { HorizontalExpand = true, FontColorOverride = Accent };
    private readonly Label _occupants = new();
    private readonly ProgressBar _podProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _timer = new();
    private readonly Label _error = new() { FontColorOverride = Color.OrangeRed, HorizontalExpand = true };

    private readonly Button _modeTarget = new() { ToggleMode = true, HorizontalExpand = true };
    private readonly Button _modeCapture = new() { ToggleMode = true, HorizontalExpand = true };
    private readonly Label _targetLabel = new();
    private readonly Button _launch = new();
    private readonly Button _abort = new();

    private readonly Button _release = new();
    private readonly ProgressBar _captureProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _captureStatus = new() { HorizontalExpand = true };
    private readonly Button _launchCaptured = new();

    private DropPodConsoleState? _state;
    private ClickMode _mode = ClickMode.Target;
    private Vector2? _target;
    private bool _mapReady;
    private TimeSpan _nextMapObjects;

    public event Action<Vector2>? OnLaunch;
    public event Action? OnLaunchToCaptured;
    public event Action? OnAbort;
    public event Action<NetEntity>? OnCapture;
    public event Action? OnRelease;

    public DropPodWindow()
    {
        IoCManager.InjectDependencies(this);

        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

        Title = Loc.GetString("droppod-ui-title");
        MinSize = new Vector2(820, 560);

        _modeTarget.Text = Loc.GetString("droppod-ui-mode-target");
        _modeCapture.Text = Loc.GetString("droppod-ui-mode-capture");
        _launch.Text = Loc.GetString("droppod-ui-launch");
        _launchCaptured.Text = Loc.GetString("droppod-ui-launch-captured");
        _abort.Text = Loc.GetString("droppod-ui-abort");
        _release.Text = Loc.GetString("droppod-ui-release");

        var group = new ButtonGroup(false);
        _modeTarget.Group = group;
        _modeCapture.Group = group;
        _modeTarget.Pressed = true;

        // Карта как в консоли шаттла: клик по ней выбирает точку или шаттл.
        _map.ShowFTLRange = false;
        _map.NoFTLRange = true;
        _map.ShowBeacons = false;
        _map.MarkMode = true;
        _map.MarkPlaced += OnMapClicked;
        _map.DrawOverlay += DrawOverlay;

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 280,
        };

        side.AddChild(Header(Loc.GetString("droppod-ui-section-pod")));
        side.AddChild(_status);
        side.AddChild(_occupants);
        side.AddChild(_podProgress);
        side.AddChild(_timer);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-click")));
        var modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        modes.AddChild(_modeTarget);
        modes.AddChild(_modeCapture);
        side.AddChild(modes);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-coords")));
        side.AddChild(_targetLabel);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _launch.HorizontalExpand = true;
        _abort.HorizontalExpand = true;
        buttons.AddChild(_launch);
        buttons.AddChild(_abort);
        side.AddChild(buttons);

        side.AddChild(Header(Loc.GetString("droppod-ui-section-capture")));
        side.AddChild(_captureStatus);
        side.AddChild(_captureProgress);
        _release.HorizontalExpand = true;
        side.AddChild(_release);
        _launchCaptured.ModulateSelfOverride = Good;
        side.AddChild(_launchCaptured);
        side.AddChild(_error);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        root.AddChild(_map);
        root.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { side } });
        Contents.AddChild(root);

        _modeTarget.OnPressed += _ => _mode = ClickMode.Target;
        _modeCapture.OnPressed += _ => _mode = ClickMode.Capture;

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

    #region Map

    private void OnMapClicked(MapCoordinates coords)
    {
        if (_state == null)
            return;

        if (_mode == ClickMode.Target)
        {
            _target = coords.Position;
            UpdateTargetLabel();
            UpdateButtons();
            return;
        }

        // Режим захвата: ближайший шаттл под курсором (в радиусе корпуса + небольшой запас).
        DropPodGridInfo? best = null;
        var bestDist = float.MaxValue;
        foreach (var info in _state.Grids)
        {
            var dist = (info.Position - coords.Position).Length();
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

    private void DrawOverlay(DrawingHandleScreen handle, Func<Vector2, Vector2> toControl)
    {
        if (_state is not { } state)
            return;

        // Дальность захвата вокруг консоли.
        var console = toControl(state.ConsolePosition);
        var edge = toControl(state.ConsolePosition + new Vector2(state.CaptureRange, 0f));
        var rangePx = (edge - console).Length();
        handle.DrawCircle(console, rangePx, Capturing.WithAlpha(0.04f), true);
        handle.DrawCircle(console, rangePx, Capturing.WithAlpha(0.55f), false);
        DrawLabel(handle, Loc.GetString("droppod-ui-range", ("range", (int) state.CaptureRange)), console + new Vector2(6f, -rangePx - 14f), Capturing);

        // Консоль.
        handle.DrawCircle(console, 4f, Color.Cyan, true);
        DrawLabel(handle, Loc.GetString("droppod-ui-console"), console + new Vector2(8f, 2f), Color.Cyan);

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

            var pos = toControl(info.Position);
            var radius = MathF.Max(12f, (toControl(info.Position + new Vector2(info.Radius, 0f)) - pos).Length() + 6f);
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

    /// <summary>Передаём карте сетки (шаттлы и станции), известные клиенту на карте консоли.</summary>
    private void RefreshMapObjects(MapId mapId)
    {
        var objects = new List<IMapObject>();
        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            if (!_ent.TryGetComponent(grid.Owner, out MetaDataComponent? meta))
                continue;

            objects.Add(new GridMapObject
            {
                Name = meta.EntityName,
                Entity = grid.Owner,
            });
        }

        _map.SetMapObjects(new Dictionary<MapId, List<IMapObject>> { [mapId] = objects });
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
        _abort.Disabled = !(state.Linked && state.PodState == DropPodState.Countdown);
        _release.Disabled = !captured && !capturing;
    }

    public void UpdateState(DropPodConsoleState state)
    {
        _state = state;

        var mapId = new MapId(state.MapId);
        if (!_mapReady)
        {
            _mapReady = true;
            _map.SetMap(mapId, state.ConsolePosition);
            _map.Offset = state.ConsolePosition;
            _map.SetRadarZoom(0.08f);
            UpdateTargetLabel();
            RefreshMapObjects(mapId);
        }

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

        if (_mapReady && _timing.RealTime >= _nextMapObjects)
        {
            _nextMapObjects = _timing.RealTime + TimeSpan.FromSeconds(0.5);
            RefreshMapObjects(new MapId(state.MapId));
        }

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
