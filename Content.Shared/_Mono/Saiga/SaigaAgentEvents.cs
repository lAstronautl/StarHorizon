using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Saiga;

/// <summary>
///     Sent client -> server to turn the agent brain on/off for the sender's character and set
///     its goal. The server then reacts event-driven whenever the character hears someone.
/// </summary>
[Serializable, NetSerializable]
public sealed class SaigaAgentEnableEvent : EntityEventArgs
{
    public bool Enabled { get; }
    public string Goal { get; }

    /// <summary>The player's own Ollama endpoint (inference on their machine). Empty = server default.</summary>
    public string Endpoint { get; }

    /// <summary>GPU layers for the player's Ollama. -1 = auto.</summary>
    public int NumGpu { get; }

    public SaigaAgentEnableEvent(bool enabled, string goal, string endpoint, int numGpu)
    {
        Enabled = enabled;
        Goal = goal;
        Endpoint = endpoint;
        NumGpu = numGpu;
    }
}

/// <summary>
///     Sent server -> client: the action the agent should perform this turn.
/// </summary>
[Serializable, NetSerializable]
public sealed class SaigaAgentDecisionResponseEvent : EntityEventArgs
{
    /// <summary>What to say out loud, or null/empty to stay silent.</summary>
    public string? Say { get; }

    /// <summary>"follow" / "stop" / "none" / "craft" / "construct" / "use_on" / ...</summary>
    public string? Action { get; }

    /// <summary>Target entity for the action (who to follow, what to pick up / use on, ...).</summary>
    public NetEntity? Target { get; }

    /// <summary>String payload for the action — e.g. the construction recipe id for craft/construct.</summary>
    public string? Arg { get; }

    public SaigaAgentDecisionResponseEvent(string? say, string? action, NetEntity? target, string? arg = null)
    {
        Say = say;
        Action = action;
        Target = target;
        Arg = arg;
    }
}
