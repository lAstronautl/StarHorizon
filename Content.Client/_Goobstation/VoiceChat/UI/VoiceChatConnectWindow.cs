using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Goobstation.VoiceChat.UI;

public sealed class VoiceChatConnectWindow : DefaultWindow
{
    public VoiceChatConnectWindow()
    {
        Title = Loc.GetString("ui-escape-voice-chat");
        MinSize = new Vector2(480, 220);
        Contents.AddChild(new VoiceChatConnectPanel { Margin = new Thickness(8) });
    }
}
