using System.Collections.Generic;

namespace Content.Server._Mono.Saiga.Mcp;

/// <summary>
///     Buffer of recent speech the agent has heard, captured from <c>ListenEvent</c> independently
///     of the C# brain. The MCP <c>listen</c> tool drains it, so an external LLM driving the agent
///     can react to what players say — MCP-first speech reactivity without the built-in brain.
/// </summary>
[RegisterComponent]
public sealed partial class SaigaHearingComponent : Component
{
    /// <summary>Recent heard lines, oldest first, bounded.</summary>
    public List<HeardLine> Lines = new();
}

/// <summary>One overheard utterance.</summary>
public sealed class HeardLine
{
    public NetEntity Net;   // the speaker's network id — so the agent can follow/approach them directly
    public string Speaker = string.Empty;
    public string Text = string.Empty;
    public TimeSpan Time;
    public bool Read;   // already returned by a previous `listen` call
}
