using Robust.Shared.Configuration;

namespace Content.Shared._Mono.Saiga;

/// <summary>
///     CVars for the MCP (Model Context Protocol) server that exposes the agent's game tools
///     over JSON-RPC, so external LLM clients (Claude, GPT, ...) can drive the embodied agent.
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class SaigaMcpCVars
{
    /// <summary>
    ///     Master switch for the MCP endpoint. When false (default) the <c>/mcp</c> path responds 404.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("saiga.mcp.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Bearer token required in the <c>Authorization</c> header. Empty (default) keeps the
    ///     endpoint closed (fail-closed) — set a non-empty secret to enable access.
    /// </summary>
    public static readonly CVarDef<string> Token =
        CVarDef.Create("saiga.mcp.token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
