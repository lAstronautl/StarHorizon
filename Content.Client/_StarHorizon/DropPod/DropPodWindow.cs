using System.Globalization;
using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>Окно консоли дроп-пода: захват шаттла, координаты цели, запуск и отмена.</summary>
public sealed class DropPodWindow : DefaultWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly Color Accent = Color.FromHex("#ff9a30");
    private static readonly Color Good = Color.FromHex("#5fd35f");

    private readonly Label _status = new() { HorizontalExpand = true, FontColorOverride = Accent };
    private readonly Label _occupants = new();
    private readonly ProgressBar _podProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _timer = new();
    private readonly Label _error = new() { FontColorOverride = Color.OrangeRed };

    private readonly OptionButton _grids = new() { HorizontalExpand = true };
    private readonly Button _capture = new();
    private readonly Button _release = new();
    private readonly ProgressBar _captureProgress = new() { MinHeight = 14, MaxValue = 1f, HorizontalExpand = true };
    private readonly Label _captureStatus = new();

    private readonly LineEdit _x = new() { HorizontalExpand = true, PlaceHolder = "X" };
    private readonly LineEdit _y = new() { HorizontalExpand = true, PlaceHolder = "Y" };
    private readonly Button _here = new();
    private readonly Button _launch = new();
    private readonly Button _launchCaptured = new();
    private readonly Button _abort = new();

    private readonly List<NetEntity> _gridIds = new();
    private DropPodConsoleState? _state;
    private bool _filled;

    public event Action<Vector2>? OnLaunch;
    public event Action? OnLaunchToCaptured;
    public event Action? OnAbort;
    public event Action<NetEntity>? OnCapture;
    public event Action? OnRelease;

    public DropPodWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("droppod-ui-title");
        MinSize = new Vector2(420, 400);

        _here.Text = Loc.GetString("droppod-ui-here");
        _launch.Text = Loc.GetString("droppod-ui-launch");
        _launchCaptured.Text = Loc.GetString("droppod-ui-launch-captured");
        _abort.Text = Loc.GetString("droppod-ui-abort");
        _capture.Text = Loc.GetString("droppod-ui-capture");
        _release.Text = Loc.GetString("droppod-ui-release");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
        };

        root.AddChild(Header(Loc.GetString("droppod-ui-section-pod")));
        root.AddChild(_status);
        root.AddChild(_occupants);
        root.AddChild(_podProgress);
        root.AddChild(_timer);

        root.AddChild(Header(Loc.GetString("droppod-ui-section-capture")));
        root.AddChild(_grids);
        var captureButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _capture.HorizontalExpand = true;
        _release.HorizontalExpand = true;
        captureButtons.AddChild(_capture);
        captureButtons.AddChild(_release);
        root.AddChild(captureButtons);
        root.AddChild(_captureProgress);
        root.AddChild(_captureStatus);
        _launchCaptured.ModulateSelfOverride = Good;
        root.AddChild(_launchCaptured);

        root.AddChild(Header(Loc.GetString("droppod-ui-section-coords")));
        var coords = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        coords.AddChild(new Label { Text = "X" });
        coords.AddChild(_x);
        coords.AddChild(new Label { Text = "Y" });
        coords.AddChild(_y);
        root.AddChild(coords);
        root.AddChild(_here);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _launch.HorizontalExpand = true;
        _abort.HorizontalExpand = true;
        buttons.AddChild(_launch);
        buttons.AddChild(_abort);
        root.AddChild(buttons);
        root.AddChild(_error);

        Contents.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { root } });

        _here.OnPressed += _ =>
        {
            if (_state != null)
                SetCoordinates(_state.ConsolePosition);
        };

        _launch.OnPressed += _ =>
        {
            if (TryParse(_x.Text, out var x) && TryParse(_y.Text, out var y))
                OnLaunch?.Invoke(new Vector2(x, y));
            else
                _error.Text = Loc.GetString("droppod-error-bad-target");
        };

        _launchCaptured.OnPressed += _ => OnLaunchToCaptured?.Invoke();
        _abort.OnPressed += _ => OnAbort?.Invoke();
        _release.OnPressed += _ => OnRelease?.Invoke();
        _capture.OnPressed += _ =>
        {
            var idx = _grids.SelectedId;
            if (idx >= 0 && idx < _gridIds.Count)
                OnCapture?.Invoke(_gridIds[idx]);
        };
        _grids.OnItemSelected += args => _grids.SelectId(args.Id);
    }

    private static Control Header(string text)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 6, 0, 0) };
        box.AddChild(new Label { Text = text, FontColorOverride = Accent });
        box.AddChild(new PanelContainer { MinHeight = 2, PanelOverride = new Robust.Client.Graphics.StyleBoxFlat { BackgroundColor = Accent } });
        return box;
    }

    private static bool TryParse(string text, out float value)
    {
        return float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && float.IsFinite(value);
    }

    private void SetCoordinates(Vector2 pos)
    {
        _x.Text = MathF.Round(pos.X).ToString(CultureInfo.InvariantCulture);
        _y.Text = MathF.Round(pos.Y).ToString(CultureInfo.InvariantCulture);
    }

    public void UpdateState(DropPodConsoleState state)
    {
        _state = state;

        if (!_filled)
        {
            _filled = true;
            SetCoordinates(state.ConsolePosition);
        }

        _error.Text = state.Error ?? string.Empty;

        if (!state.Linked)
        {
            _status.Text = Loc.GetString("droppod-ui-no-pod");
            _occupants.Text = string.Empty;
        }
        else
        {
            _status.Text = Loc.GetString("droppod-ui-state", ("state", Loc.GetString($"droppod-state-{state.PodState}")));
            _occupants.Text = Loc.GetString("droppod-ui-occupants", ("count", state.Occupants));
        }

        RebuildGrids(state);

        var idle = state.Linked && state.PodState == DropPodState.Idle;
        var captured = state.CapturedGrid != null;
        var capturing = state.CapturingGrid != null;

        _launch.Disabled = !idle;
        _launchCaptured.Disabled = !idle || !captured;
        _abort.Disabled = !(state.Linked && state.PodState == DropPodState.Countdown);
        _x.Editable = idle;
        _y.Editable = idle;
        _here.Disabled = !idle;
        _capture.Disabled = capturing || _gridIds.Count == 0;
        _release.Disabled = !captured && !capturing;
        _grids.Disabled = capturing;

        if (captured)
        {
            _captureStatus.FontColorOverride = Good;
            _captureStatus.Text = Loc.GetString("droppod-ui-captured", ("name", GridName(state, state.CapturedGrid!.Value)));
        }
        else
        {
            _captureStatus.FontColorOverride = null;
            _captureStatus.Text = capturing
                ? Loc.GetString("droppod-ui-capturing", ("name", GridName(state, state.CapturingGrid!.Value)))
                : Loc.GetString("droppod-ui-not-captured");
        }
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

    private void RebuildGrids(DropPodConsoleState state)
    {
        var selected = _grids.SelectedId >= 0 && _grids.SelectedId < _gridIds.Count
            ? _gridIds[_grids.SelectedId]
            : (NetEntity?) null;

        _gridIds.Clear();
        _grids.Clear();

        var index = 0;
        foreach (var info in state.Grids)
        {
            _gridIds.Add(info.Grid);
            _grids.AddItem($"{info.Name} ({MathF.Round(info.Position.X)}, {MathF.Round(info.Position.Y)})", index);
            if (info.Grid == selected || info.Grid == state.CapturingGrid || info.Grid == state.CapturedGrid)
                _grids.SelectId(index);
            index++;
        }

        if (selected == null && index > 0 && state.CapturingGrid == null && state.CapturedGrid == null)
            _grids.SelectId(0);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_state is not { } state)
            return;

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
