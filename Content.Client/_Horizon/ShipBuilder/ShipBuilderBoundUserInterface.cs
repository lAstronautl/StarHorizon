using Content.Shared._Horizon.ShipBuilder;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.ShipBuilder;

[UsedImplicitly]
public sealed class ShipBuilderBoundUserInterface : BoundUserInterface
{
    private ShipBuilderWindow? _window;

    public ShipBuilderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ShipBuilderWindow>();
        _window.OnPlace += (world, tool, id) => SendMessage(new ShipBuilderPlaceMessage(world, tool, id));
        _window.OnMoveCamera += delta => SendMessage(new ShipBuilderMoveCameraMessage(delta));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ShipBuilderBuiState cast)
            _window?.UpdateState(cast);
    }
}
