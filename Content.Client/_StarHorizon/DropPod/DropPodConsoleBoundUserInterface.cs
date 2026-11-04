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
        _window.OnAbort += () => SendMessage(new DropPodAbortMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DropPodConsoleState cast)
            _window?.UpdateState(cast);
    }
}
