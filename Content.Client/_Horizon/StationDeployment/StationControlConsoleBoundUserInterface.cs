using Content.Shared._Horizon.StationDeployment;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.StationDeployment;

public sealed class StationControlConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private StationControlConsoleWindow? _window;

    public StationControlConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StationControlConsoleWindow>();
        _window.OnNameChange += name => SendMessage(new StationControlConsoleRenameMessage(name));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not StationControlConsoleBuiState cast || _window == null)
            return;

        _window.UpdateState(cast.StationName);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _window?.Close();
    }
}
