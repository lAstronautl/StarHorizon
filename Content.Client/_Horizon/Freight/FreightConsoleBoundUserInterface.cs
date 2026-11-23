using Content.Shared._Horizon.Freight;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.Freight;

public sealed class FreightConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FreightConsoleWindow? _window;

    public FreightConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FreightConsoleWindow>();
        _window.OnSpawnPressed += () => SendMessage(new FreightSpawnMessage());
        _window.OnSellPressed += () => SendMessage(new FreightSellMessage());
        _window.OnRefreshPressed += () => SendMessage(new FreightRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is FreightConsoleBuiState cast)
            _window?.UpdateState(cast);
    }
}
