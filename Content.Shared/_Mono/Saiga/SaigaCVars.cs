using Robust.Shared.Configuration;

namespace Content.Shared._Mono.Saiga;

/// <summary>
///     CVars for the Saiga (local LLM via Ollama) integration.
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class SaigaCVars
{
    /// <summary>
    ///     Master switch for the Saiga/Ollama integration.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("saiga.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Base URL of the Ollama HTTP API (no trailing slash).
    /// </summary>
    public static readonly CVarDef<string> ApiUrl =
        CVarDef.Create("saiga.api_url", "http://localhost:11434", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Backend wire format: "ollama" (native /api/chat) or "openai" (/chat/completions,
    ///     for LM Studio / vLLM / llama.cpp / OpenAI). For "openai" set api_url to the base
    ///     ending in /v1, e.g. http://localhost:1234/v1.
    /// </summary>
    public static readonly CVarDef<string> ApiFormat =
        CVarDef.Create("saiga.api_format", "ollama", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Bearer token sent as "Authorization: Bearer &lt;key&gt;" when saiga.api_format is
    ///     "openai" — for cloud/paid OpenAI-compatible backends (OpenRouter, Google AI Studio's
    ///     OpenAI-compat endpoint, OpenAI itself, ...). Empty (default) sends no auth header, for
    ///     local unauthenticated servers (LM Studio, Ollama, vLLM). Ignored for api_format=ollama.
    /// </summary>
    public static readonly CVarDef<string> ApiKey =
        CVarDef.Create("saiga.api_key", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Ollama model tag to use for completions.
    /// </summary>
    public static readonly CVarDef<string> Model =
        CVarDef.Create("saiga.model", "hf.co/QuantFactory/saiga_gemma2_9b-GGUF:Q4_K_S", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Default system prompt prepended to every chat request.
    /// </summary>
    public static readonly CVarDef<string> SystemPrompt =
        CVarDef.Create("saiga.system_prompt",
            "Ты — персонаж космической станции в игре Space Station 14. Отвечай кратко, по-русски, оставаясь в роли.",
            CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Request timeout in seconds.
    /// </summary>
    public static readonly CVarDef<int> Timeout =
        CVarDef.Create("saiga.timeout", 30, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Sampling temperature (0.0 - 2.0).
    /// </summary>
    public static readonly CVarDef<float> Temperature =
        CVarDef.Create("saiga.temperature", 0.4f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Maximum number of tokens to generate per response (Ollama num_predict).
    /// </summary>
    public static readonly CVarDef<int> MaxTokens =
        CVarDef.Create("saiga.max_tokens", 256, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Ollama context window (num_ctx). Smaller = less VRAM for the KV cache, so the model is
    ///     more likely to fit entirely on the GPU. Our prompts are ~1-1.5k tokens.
    /// </summary>
    public static readonly CVarDef<int> NumCtx =
        CVarDef.Create("saiga.num_ctx", 2048, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Forces how many model layers Ollama offloads to the GPU (num_gpu). A high value
    ///     (e.g. 999) puts the whole model on the GPU; -1 lets Ollama decide automatically.
    ///     Use if Ollama leaves layers on the CPU despite free VRAM. Too high + not enough VRAM = OOM.
    /// </summary>
    public static readonly CVarDef<int> NumGpu =
        CVarDef.Create("saiga.num_gpu", -1, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     If non-empty, every model request appends a row to this CSV file (research metrics).
    ///     Example: /tmp/saiga_metrics.csv . Empty = disabled.
    /// </summary>
    public static readonly CVarDef<string> MetricsPath =
        CVarDef.Create("saiga.metrics_path", "", CVar.SERVERONLY);

    /// <summary>
    ///     If non-empty, every dialogue turn and action is appended to this JSONL file
    ///     (full history: heard text, reply, action, target). Empty = disabled.
    /// </summary>
    public static readonly CVarDef<string> TranscriptPath =
        CVarDef.Create("saiga.transcript_path", "", CVar.SERVERONLY);

    // --- Client-side AI agent (a client that pilots its own character) ---

    /// <summary>
    ///     Ollama base URL the client agent talks to (no trailing slash). Client-side.
    /// </summary>
    public static readonly CVarDef<string> AgentApiUrl =
        CVarDef.Create("saiga.agent.api_url", "http://localhost:11434", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Ollama model the client agent uses for decisions. Client-side.
    /// </summary>
    public static readonly CVarDef<string> AgentModel =
        CVarDef.Create("saiga.agent.model", "hf.co/QuantFactory/saiga_gemma2_9b-GGUF:Q4_K_S", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds between agent decisions (perceive -> decide -> act). Client-side.
    /// </summary>
    public static readonly CVarDef<float> AgentInterval =
        CVarDef.Create("saiga.agent.interval", 4f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How far (tiles) the agent perceives entities around it. Client-side.
    /// </summary>
    public static readonly CVarDef<float> AgentRange =
        CVarDef.Create("saiga.agent.range", 8f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     If true, the client auto-joins the round and enables the agent on connect.
    ///     For running a headless AI client with no UI/console. Client-side.
    /// </summary>
    public static readonly CVarDef<bool> AgentAutoStart =
        CVarDef.Create("saiga.agent.autostart", false, CVar.CLIENTONLY);

    /// <summary>
    ///     Goal the agent starts with (used by autostart). Client-side.
    /// </summary>
    public static readonly CVarDef<string> AgentGoal =
        CVarDef.Create("saiga.agent.goal",
            "Веди себя как обычный член экипажа: отвечай, когда к тебе обращаются, и иди за тем, кто просит.",
            CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     The player's OWN Ollama endpoint, sent to the server so inference runs on THIS player's
    ///     machine (decentralized). Empty (default) = use the server's own saiga.api_url — don't
    ///     silently redirect every agent to localhost when nobody configured this.
    /// </summary>
    public static readonly CVarDef<string> AgentEndpoint =
        CVarDef.Create("saiga.agent.endpoint", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     GPU layers (num_gpu) for THIS player's Ollama, sent to the server. -1 = let Ollama decide.
    ///     Lets each player tune to their own VRAM. Client-side.
    /// </summary>
    public static readonly CVarDef<int> AgentNumGpu =
        CVarDef.Create("saiga.agent.num_gpu", -1, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     If true, the server honours a client-supplied Ollama endpoint (inference on the player's
    ///     machine). Disable on shared servers if you don't want the server making outbound requests
    ///     to player-controlled URLs (SSRF). Server-side.
    /// </summary>
    public static readonly CVarDef<bool> AllowClientEndpoint =
        CVarDef.Create("saiga.allow_client_endpoint", true, CVar.SERVERONLY | CVar.ARCHIVE);
}
