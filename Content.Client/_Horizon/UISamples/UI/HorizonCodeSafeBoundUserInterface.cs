// Сгенерировано SS14 UI Editor (предмет «кодовый сейф»). Не редактируйте вручную.
using System;
using Content.Shared._Horizon.UIItems;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Horizon.UISamples.UI;

/// <summary>Связывает предмет HorizonCodeSafe с окном CodeSafeWindow.</summary>
public sealed class HorizonCodeSafeBoundUserInterface : BoundUserInterface
{
    private CodeSafeWindow? _window;

    public HorizonCodeSafeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CodeSafeWindow>();
        _window.ServerSend = (action, value) => SendMessage(new HorizonCodeSafeUiMessage(action, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HorizonCodeSafeUiState s)
            _window?.ApplyServerState(s.Values);
    }
}
