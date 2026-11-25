// Сгенерировано SS14 UI Editor (предмет «грузовой терминал»). Не редактируйте вручную.
using System;
using Content.Shared._Horizon.UIItems;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Horizon.UISamples.UI;

/// <summary>Связывает предмет HorizonCargoTerminal с окном CargoRequestWindow.</summary>
public sealed class HorizonCargoTerminalBoundUserInterface : BoundUserInterface
{
    private CargoRequestWindow? _window;

    public HorizonCargoTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CargoRequestWindow>();
        _window.ServerSend = (action, value) => SendMessage(new HorizonCargoTerminalUiMessage(action, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HorizonCargoTerminalUiState s)
            _window?.ApplyServerState(s.Values);
    }
}
