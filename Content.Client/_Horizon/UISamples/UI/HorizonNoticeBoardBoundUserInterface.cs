// Сгенерировано SS14 UI Editor (предмет «доска объявлений»). Не редактируйте вручную.
using System;
using Content.Shared._Horizon.UIItems;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client._Horizon.UISamples.UI;

/// <summary>Связывает предмет HorizonNoticeBoard с окном NoticeBoardWindow.</summary>
public sealed class HorizonNoticeBoardBoundUserInterface : BoundUserInterface
{
    private NoticeBoardWindow? _window;

    public HorizonNoticeBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NoticeBoardWindow>();
        _window.ServerSend = (action, value) => SendMessage(new HorizonNoticeBoardUiMessage(action, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HorizonNoticeBoardUiState s)
            _window?.ApplyServerState(s.Values);
    }
}
