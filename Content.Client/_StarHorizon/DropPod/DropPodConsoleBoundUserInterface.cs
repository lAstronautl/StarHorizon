using Content.Shared._StarHorizon.DropPod;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._StarHorizon.DropPod;

[UsedImplicitly]
public sealed class DropPodConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private DropPodWindow? _window;

    public DropPodConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DropPodWindow>();
        _window.OnLaunch += target => SendMessage(new DropPodLaunchMessage(target));
        _window.OnLaunchToCaptured += () => SendMessage(new DropPodLaunchToCapturedMessage());
        _window.OnAbort += () => SendMessage(new DropPodAbortMessage());
        _window.OnCapture += grid => SendMessage(new DropPodCaptureMessage(grid));
        _window.OnRelease += () => SendMessage(new DropPodReleaseMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DropPodConsoleState cast)
            _window?.UpdateState(cast);
    }
}
