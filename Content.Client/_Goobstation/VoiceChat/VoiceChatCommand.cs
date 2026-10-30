using Content.Shared._Goobstation.CCVar;
using Robust.Shared.Configuration;
using Content.Client._Goobstation.VoiceChat.UI;
using Robust.Client.UserInterface;
using Robust.Shared.Console;

namespace Content.Client._Goobstation.VoiceChat;

public sealed partial class VoiceChatCommand : LocalizedCommands
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public override string Command => "voicechat";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_cfg.GetCVar(GoobCVars.VoiceChatEnabled))
        {
            shell.WriteError(Loc.GetString("cmd-voicechat-unavailable"));
            return;
        }

        _ui.GetUIController<VoiceChatUIController>().Open();
    }
}
