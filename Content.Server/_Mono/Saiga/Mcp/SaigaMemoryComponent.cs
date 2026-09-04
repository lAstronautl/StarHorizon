using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._Mono.Saiga.Mcp;

/// <summary>
///     Per-agent world-memory graph, built deterministically (no LLM) as a side effect of the MCP
///     <c>observe</c> tool. Lets an external LLM act on accumulated knowledge via <c>recall</c> /
///     <c>where_is</c> instead of re-perceiving every time — the whole point on weak hardware.
///
///     Nodes = entities the agent has seen (name, category, last-known map position, last-seen tick).
///     Edges = <see cref="MemNode.Near"/>: which other nodes were within a couple metres at the last
///     sighting (the "together" / spatial-adjacency relation). Server-only; never networked.
/// </summary>
[RegisterComponent]
public sealed partial class SaigaMemoryComponent : Component
{
    /// <summary>Remembered entities, keyed by their network id.</summary>
    public Dictionary<NetEntity, MemNode> Nodes = new();
}

/// <summary>One remembered entity in <see cref="SaigaMemoryComponent"/>.</summary>
public sealed class MemNode
{
    public NetEntity Net;
    public string Name = string.Empty;
    public string Category = string.Empty;   // персонаж / существо / предмет / инструмент / объект
    public string Tool = string.Empty;        // tool qualities (Prying, Anchoring, ...) if it's a tool
    public Vector2 Pos;                       // last-known map position
    public MapId MapId;
    public TimeSpan LastSeen;
    public HashSet<NetEntity> Near = new();   // graph edges: nodes seen nearby at last sighting
}
