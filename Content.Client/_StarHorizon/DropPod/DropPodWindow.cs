using System.Globalization;
using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>Окно консоли дроп-пода: координаты цели, запуск и отмена.</summary>
public sealed class DropPodWindow : DefaultWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Label _status = new() { HorizontalExpand = true };
    private readonly Label _timer = new();
    private readonly Label _error = new() { Modulate = Color.OrangeRed };
    private readonly Label _occupants = new();
    private readonly LineEdit _x = new() { HorizontalExpand = true, PlaceHolder = "X" };
    private readonly LineEdit _y = new() { HorizontalExpand = true, PlaceHolder = "Y" };
    private readonly Button _here = new();
    private readonly Button _launch = new();
    private readonly Button _abort = new();

    private DropPodConsoleState? _state;
    private bool _filled;

    public event Action<Vector2>? OnLaunch;
    public event Action? OnAbort;

    public DropPodWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("droppod-ui-title");
        MinSize = new Vector2(360, 260);

        _here.Text = Loc.GetString("droppod-ui-here");
        _launch.Text = Loc.GetString("droppod-ui-launch");
        _abort.Text = Loc.GetString("droppod-ui-abort");

        var coords = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        coords.AddChild(new Label { Text = "X" });
        coords.AddChild(_x);
        coords.AddChild(new Label { Text = "Y" });
        coords.AddChild(_y);

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _launch.HorizontalExpand = true;
        _abort.HorizontalExpand = true;
        buttons.AddChild(_launch);
        buttons.AddChild(_abort);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
        };
        root.AddChild(_status);
        root.AddChild(_occupants);
        root.AddChild(_timer);
        root.AddChild(new Label { Text = Loc.GetString("droppod-ui-target") });
        root.AddChild(coords);
        root.AddChild(_here);
        root.AddChild(buttons);
        root.AddChild(_error);
        Contents.AddChild(root);

        _here.OnPressed += _ =>
        {
            if (_state == null)
                return;

            SetCoordinates(_state.ConsolePosition);
        };

        _launch.OnPressed += _ =>
        {
            if (TryParse(_x.Text, out var x) && TryParse(_y.Text, out var y))
            {
                OnLaunch?.Invoke(new Vector2(x, y));
            }
            else
            {
                _error.Text = Loc.GetString("droppod-error-bad-target");
            }
        };

        _abort.OnPressed += _ => OnAbort?.Invoke();
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

        var idle = state.Linked && state.PodState == DropPodState.Idle;
        _launch.Disabled = !idle;
        _abort.Disabled = !(state.Linked && state.PodState == DropPodState.Countdown);
        _x.Editable = idle;
        _y.Editable = idle;
        _here.Disabled = !idle;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_state is not { Linked: true } state || state.PodState == DropPodState.Idle)
        {
            _timer.Text = string.Empty;
            return;
        }

        var left = Math.Max(0, (state.StateEnd - _timing.CurTime).TotalSeconds);
        _timer.Text = Loc.GetString("droppod-ui-timer", ("seconds", left.ToString("0.0", CultureInfo.InvariantCulture)));
    }
}
