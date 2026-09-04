namespace Content.Server._Mono.Saiga;

/// <summary>
///     Per-entity memory for the client AI agent's brain: what it recently overheard and the
///     running dialogue history. Added automatically by <see cref="SaigaAgentBrainSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class SaigaAgentStateComponent : Component
{
    /// <summary>Recently overheard lines ("Имя: текст"), oldest first.</summary>
    public readonly List<string> HeardSpeech = new();

    /// <summary>Rolling dialogue history (user/assistant turns) fed back to the model.</summary>
    public readonly List<SaigaMessage> History = new();

    /// <summary>The entity that most recently spoke near us — used as the follow target.</summary>
    public EntityUid? LastSpeaker;

    /// <summary>Free-text goal that steers decisions.</summary>
    public string Goal = "";

    /// <summary>Whether the agent brain is active for this character.</summary>
    public bool AgentEnabled;

    /// <summary>True while a model request is in flight.</summary>
    public bool Busy;

    /// <summary>Earliest time we may react again (anti-spam).</summary>
    public TimeSpan NextResponse;

    /// <summary>This player's Ollama endpoint (inference on their machine). Empty = server default.</summary>
    public string Endpoint = "";

    /// <summary>GPU layers for this player's Ollama. -1 = auto.</summary>
    public int NumGpu = -1;
}
