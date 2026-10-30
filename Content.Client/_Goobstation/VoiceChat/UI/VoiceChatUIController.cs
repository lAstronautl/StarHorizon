using Robust.Client.UserInterface.Controllers;

namespace Content.Client._Goobstation.VoiceChat.UI;

public sealed class VoiceChatUIController : UIController
{
    private VoiceChatConnectWindow? _window;

    public void Open()
    {
        if (_window is { Disposed: false })
        {
            _window.MoveToFront();
            return;
        }

        _window = new VoiceChatConnectWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }
}
