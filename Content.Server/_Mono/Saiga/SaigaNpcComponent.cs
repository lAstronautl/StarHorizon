namespace Content.Server._Mono.Saiga;

/// <summary>
///     Makes an entity overhear nearby speech and reply using the local Saiga (Ollama) model.
///     Replies are rate-limited by <see cref="Cooldown"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(SaigaNpcSystem))]
public sealed partial class SaigaNpcComponent : Component
{
    /// <summary>
    ///     Personality / instructions sent to the model as the system prompt.
    /// </summary>
    [DataField]
    public string SystemPrompt =
        "Ты — обычный член экипажа космической станции в игре Space Station 14. " +
        "Отвечай коротко (1-2 предложения), по-русски, живо и оставаясь в роли. " +
        "Не выходи из образа и не упоминай, что ты ИИ.";

    /// <summary>
    ///     How far (in tiles) the NPC can overhear speech.
    /// </summary>
    [DataField]
    public float Range = 8f;

    /// <summary>
    ///     Minimum seconds between two replies. Prevents spamming the model and the chat.
    /// </summary>
    [DataField]
    public float Cooldown = 8f;

    /// <summary>
    ///     If true, reply with a whisper instead of normal speech.
    /// </summary>
    [DataField]
    public bool Whisper;

    /// <summary>
    ///     If true, only react to speech from real players (entities with an actor),
    ///     ignoring other NPCs and machines.
    /// </summary>
    [DataField]
    public bool RespondToPlayersOnly = true;

    /// <summary>
    ///     How many past exchanges (user+assistant pairs) to keep as conversation memory.
    /// </summary>
    [DataField]
    public int MaxHistory = 4;

    /// <summary>
    ///     Earliest time the NPC is allowed to reply again. Runtime state.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextResponse = TimeSpan.Zero;

    /// <summary>
    ///     True while a request to the model is in flight. Runtime state.
    /// </summary>
    [ViewVariables]
    public bool Busy;

    /// <summary>
    ///     Rolling conversation history (system prompt excluded). Runtime state.
    /// </summary>
    [ViewVariables]
    public readonly List<SaigaMessage> History = new();
}
