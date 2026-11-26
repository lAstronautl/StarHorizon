// Сгенерировано SS14 UI Editor (предмет «счётчик посетителей»). Не редактируйте вручную.
using System;
using Content.Shared._Horizon.UIItems;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Horizon.UISamples.UI;

/// <summary>Связывает предмет HorizonVisitorCounter с окном VisitorCounterWindow.</summary>
public sealed class HorizonVisitorCounterBoundUserInterface : BoundUserInterface
{
    private VisitorCounterWindow? _window;

    public HorizonVisitorCounterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<VisitorCounterWindow>();
        _window.ServerSend = (action, value) => SendMessage(new HorizonVisitorCounterUiMessage(action, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HorizonVisitorCounterUiState s)
            _window?.ApplyServerState(s.Values);
    }
}
