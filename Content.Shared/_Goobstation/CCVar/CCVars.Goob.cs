using Robust.Shared.Configuration;

namespace Content.Shared._Goobstation.CCVar;

[CVarDefs]
public sealed partial class GoobCVars
{

    /// <summary>
    ///     Goobstation: The amount of time between NPC Silicons draining their battery in seconds.
    /// </summary>
    public static readonly CVarDef<float> SiliconNpcUpdateTime =
        CVarDef.Create("silicon.npcupdatetime", 1.5f, CVar.SERVERONLY);

    #region Voicechat

    public static readonly CVarDef<bool> VoiceChatEnabled =
        CVarDef.Create("voice.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE, "Whether proximity voice chat is enabled.");

    public static readonly CVarDef<float> VoiceChatRange =
        CVarDef.Create("voice.range", 10f, CVar.SERVER | CVar.REPLICATED, "Distance in tiles at which voice chat fades out completely.");

    public static readonly CVarDef<string> VoiceChatWebSocketBind =
        CVarDef.Create("voice.ws_bind", "127.0.0.1:1213", CVar.SERVERONLY, "Address and port the voice chat WebSocket listens on. Expose it through the reverse proxy in front of the status port at /voice/ws.");

    public static readonly CVarDef<string> VoiceChatTrustedProxies =
        CVarDef.Create("voice.trusted_proxies", "", CVar.SERVERONLY, "Comma-separated proxy IPs whose X-Real-IP and X-Forwarded-For headers are trusted. Loopback and private addresses are always trusted.");

    public static readonly CVarDef<int> VoiceChatMaxConnectionsPerIp =
        CVarDef.Create("voice.max_connections_per_ip", 6, CVar.SERVERONLY, "Maximum simultaneous voice chat connections from one IP address.");

    public static readonly CVarDef<string> VoiceChatPublicUrl =
        CVarDef.Create("voice.public_url", "", CVar.SERVERONLY, "Public URL of the voice chat page, e.g. https://example.com/voice/. Derived from hub.server_url when empty.");

    public static readonly CVarDef<string> VoiceChatWebSocketUrl =
        CVarDef.Create("voice.ws_url", "", CVar.SERVERONLY, "WebSocket URL the voice chat page connects to. Auto-detected by the page when empty.");

    public static readonly CVarDef<bool> VoiceChatRadioEnabled =
        CVarDef.Create("voice.radio_enabled", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE, "Whether players can speak over radio channels with voice chat.");

    public static readonly CVarDef<bool> VoiceChatRadioCommon =
        CVarDef.Create("voice.radio_common", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE, "Whether voice chat is allowed on the Common radio channel.");

    public static readonly CVarDef<int> VoiceChatBitrate =
        CVarDef.Create("voice.bitrate", 64, CVar.SERVERONLY | CVar.ARCHIVE, "Voice bitrate sent to each client in kbps: 64 (16 kHz, best), 48, 32 (8 kHz), 24 or 16. Lower saves bandwidth at the cost of quality.");

    public static readonly CVarDef<bool> VoiceLogEnabled =
        CVarDef.Create("voice.log_enabled", true, CVar.SERVERONLY | CVar.ARCHIVE, "Whether voice chat audio is recorded to disk for admin review.");

    public static readonly CVarDef<int> VoiceLogRounds =
        CVarDef.Create("voice.log_rounds", 3, CVar.SERVERONLY | CVar.ARCHIVE, "How many rounds of voice chat recordings to keep on disk.");

    public static readonly CVarDef<bool> VoiceChatLobby =
        CVarDef.Create("voice.lobby", true, CVar.SERVERONLY | CVar.ARCHIVE, "Whether players in the lobby can talk to everyone else in the lobby.");

    public static readonly CVarDef<bool> VoiceChatDynamicRange =
        CVarDef.Create("voice.dynamic_range", true, CVar.SERVERONLY | CVar.ARCHIVE, "Whether shouting carries further and whispering stays close, based on each speaker's own normal loudness.");

    public static readonly CVarDef<float> VoiceChatShoutRange =
        CVarDef.Create("voice.shout_range", 15f, CVar.SERVERONLY | CVar.ARCHIVE, "Distance in tiles that shouted voice carries.");

    public static readonly CVarDef<float> VoiceChatWhisperRange =
        CVarDef.Create("voice.whisper_range", 3f, CVar.SERVERONLY | CVar.ARCHIVE, "Distance in tiles that whispered voice carries.");

    public static readonly CVarDef<float> VoiceChatShoutThreshold =
        CVarDef.Create("voice.shout_threshold", 14f, CVar.SERVERONLY | CVar.ARCHIVE, "How many dB above a speaker's normal loudness counts as shouting.");

    public static readonly CVarDef<float> VoiceChatWhisperThreshold =
        CVarDef.Create("voice.whisper_threshold", 5f, CVar.SERVERONLY | CVar.ARCHIVE, "How many dB below a speaker's normal loudness counts as whispering.");

    public static readonly CVarDef<bool> VoiceChatSpeakerList =
        CVarDef.Create("voice.speaker_list", true, CVar.CLIENTONLY | CVar.ARCHIVE, "Show the list of players you can currently hear on voice chat.");

    public static readonly CVarDef<float> VoiceChatRadioVolume =
        CVarDef.Create("voice.radio_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE, "Volume multiplier for voice heard over radio, 0 to 2.");

    public static readonly CVarDef<string> VoiceChatRadioMuted =
        CVarDef.Create("voice.radio_muted", "", CVar.CLIENTONLY | CVar.ARCHIVE, "Comma-separated radio channel IDs whose voice chat you don't want to hear.");

    public static readonly CVarDef<float> VoiceChatVolume =
        CVarDef.Create("voice.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE, "Voice chat playback volume, 0 to 2.");

    public static readonly CVarDef<bool> VoiceChatHearSelf =
        CVarDef.Create("voice.hear_self", false, CVar.CLIENTONLY | CVar.ARCHIVE, "Play your own voice back to you in game.");

    #endregion
}
