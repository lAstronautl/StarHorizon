using Content.Shared._Horizon.Marketplace;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.Marketplace;

public sealed class MarketplaceConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MarketplaceConsoleWindow? _window;

    public MarketplaceConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MarketplaceConsoleWindow>();
        _window.OnList += (amount, price) => SendMessage(new MarketplaceListItemMessage(amount, price));
        _window.OnBuy += (id, amount, price) => SendMessage(new MarketplaceBuyMessage(id, amount, price));
        _window.OnWithdraw += id => SendMessage(new MarketplaceWithdrawMessage(id));
        _window.OnEject += () => SendMessage(new MarketplaceEjectMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is MarketplaceConsoleState state)
            _window?.UpdateState(state);
    }
}
