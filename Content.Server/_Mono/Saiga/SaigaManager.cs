using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Mono.Saiga;
using Robust.Shared.Configuration;

namespace Content.Server._Mono.Saiga;

/// <summary>
///     Talks to a local Ollama instance running the Saiga model and returns text completions.
///     Server-side only; safe to call from EntitySystems via the IoC dependency.
/// </summary>
public sealed class SaigaManager
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly HttpClient _httpClient = new();

    private ISawmill _sawmill = default!;

    private bool _enabled;
    private string _apiUrl = string.Empty;
    private string _apiFormat = "ollama";   // "ollama" (native /api/chat) or "openai" (/chat/completions)
    private string _model = string.Empty;
    private string _systemPrompt = string.Empty;
    private int _timeout;
    private float _temperature;
    private int _maxTokens;
    private int _numCtx;
    private int _numGpu;
    private string _metricsPath = string.Empty;
    private string _transcriptPath = string.Empty;

    private readonly object _metricsLock = new();

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("saiga");

        _cfg.OnValueChanged(SaigaCVars.Enabled, v => _enabled = v, true);
        _cfg.OnValueChanged(SaigaCVars.ApiUrl, v => _apiUrl = v.TrimEnd('/'), true);
        _cfg.OnValueChanged(SaigaCVars.ApiFormat, v => _apiFormat = v.Trim().ToLowerInvariant(), true);
        _cfg.OnValueChanged(SaigaCVars.Model, v => _model = v, true);
        _cfg.OnValueChanged(SaigaCVars.SystemPrompt, v => _systemPrompt = v, true);
        _cfg.OnValueChanged(SaigaCVars.Timeout, v => _timeout = v, true);
        _cfg.OnValueChanged(SaigaCVars.Temperature, v => _temperature = v, true);
        _cfg.OnValueChanged(SaigaCVars.MaxTokens, v => _maxTokens = v, true);
        _cfg.OnValueChanged(SaigaCVars.NumCtx, v => _numCtx = v, true);
        _cfg.OnValueChanged(SaigaCVars.NumGpu, v => _numGpu = v, true);
        _cfg.OnValueChanged(SaigaCVars.MetricsPath, v => _metricsPath = v, true);
        _cfg.OnValueChanged(SaigaCVars.TranscriptPath, v => _transcriptPath = v, true);
    }

    /// <summary>
    ///     Appends one JSONL entry per dialogue turn / action when saiga.transcript_path is set.
    ///     Stores the full history (heard text, reply, action, target) for later analysis.
    /// </summary>
    public void LogTranscript(string source, string? speaker, string? heard, string? say, string? action, string? target = null, string? perception = null)
    {
        if (string.IsNullOrEmpty(_transcriptPath))
            return;

        try
        {
            var entry = new Dictionary<string, string?>
            {
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["source"] = source,
                ["speaker"] = speaker,
                ["heard"] = heard,
                ["say"] = say,
                ["action"] = action,
                ["target"] = target,
                ["perception"] = perception,
            };
            var line = JsonSerializer.Serialize(entry) + "\n";
            lock (_metricsLock)
                File.AppendAllText(_transcriptPath, line);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Saiga transcript write failed: {e.Message}");
        }
    }

    public bool Enabled => _enabled;

    /// <summary>
    ///     Sends a single user prompt and returns the assistant's reply, or null on failure.
    /// </summary>
    /// <param name="userPrompt">The user's message.</param>
    /// <param name="systemPrompt">Optional override of the configured system prompt.</param>
    public Task<string?> ChatAsync(string userPrompt, string? systemPrompt = null, CancellationToken ct = default)
    {
        var messages = new List<SaigaMessage>
        {
            new("system", systemPrompt ?? _systemPrompt),
            new("user", userPrompt),
        };
        return ChatAsync(messages, ct: ct);
    }

    /// <summary>
    ///     Sends a full message history and returns the assistant's reply, or null on failure.
    /// </summary>
    public async Task<string?> ChatAsync(IReadOnlyList<SaigaMessage> messages, bool jsonFormat = false, string source = "saiga", string? endpoint = null, int? numGpuOverride = null, CancellationToken ct = default)
    {
        // Per-player override: route to the caller's own Ollama with their own GPU layers.
        var apiUrl = string.IsNullOrWhiteSpace(endpoint) ? _apiUrl : endpoint.TrimEnd('/');
        var numGpu = numGpuOverride ?? _numGpu;

        if (!_enabled)
        {
            _sawmill.Warning("Saiga request ignored: saiga.enabled is false");
            return null;
        }

        // OpenAI-compatible backend (LM Studio / vLLM / llama.cpp / OpenAI) — leaves the Ollama path below untouched.
        if (_apiFormat == "openai")
            return await OpenAiChatAsync(messages, jsonFormat, source, apiUrl, ct);

        var reqTime = DateTime.UtcNow;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeout));

            var payload = new OllamaChatRequest
            {
                Model = _model,
                Stream = false,
                Format = jsonFormat ? "json" : null,
                Messages = messages,
                Options = new OllamaOptions
                {
                    Temperature = _temperature,
                    NumPredict = _maxTokens,
                    NumCtx = _numCtx,
                    NumGpu = numGpu >= 0 ? numGpu : null,
                },
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{apiUrl}/api/chat", content, cts.Token);
            var latencyMs = (DateTime.UtcNow - reqTime).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Error($"Saiga bad status code: {response.StatusCode}");
                WriteMetric(source, latencyMs, null, jsonFormat, false, 0, ok: false);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(body);

            var reply = parsed?.Message?.Content?.Trim();
            if (string.IsNullOrEmpty(reply))
            {
                _sawmill.Error("Saiga returned an empty response");
                WriteMetric(source, latencyMs, parsed, jsonFormat, false, 0, ok: false);
                return null;
            }

            var jsonValid = !jsonFormat || IsValidJson(reply);
            WriteMetric(source, latencyMs, parsed, jsonFormat, jsonValid, reply.Length, ok: true);

            _sawmill.Debug($"Saiga reply in {latencyMs / 1000.0:F1}s ({reply.Length} chars)");
            return reply;
        }
        catch (TaskCanceledException)
        {
            _sawmill.Error($"Saiga request timed out after {_timeout}s");
            WriteMetric(source, (DateTime.UtcNow - reqTime).TotalMilliseconds, null, jsonFormat, false, 0, ok: false);
            return null;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Saiga request error\n{e}");
            WriteMetric(source, (DateTime.UtcNow - reqTime).TotalMilliseconds, null, jsonFormat, false, 0, ok: false);
            return null;
        }
    }

    /// <summary>
    ///     OpenAI-compatible chat completion (LM Studio, vLLM, llama.cpp server, OpenAI). Mirrors
    ///     <see cref="ChatAsync(IReadOnlyList{SaigaMessage}, bool, string, string?, int?, CancellationToken)"/>
    ///     but speaks the /chat/completions schema. No response_format is sent — some servers (LM Studio)
    ///     reject json_object; the configured system prompt already asks for JSON when needed.
    /// </summary>
    private async Task<string?> OpenAiChatAsync(IReadOnlyList<SaigaMessage> messages, bool jsonFormat, string source, string apiUrl, CancellationToken ct)
    {
        var reqTime = DateTime.UtcNow;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeout));

            var payload = new OpenAiChatRequest
            {
                Model = _model,
                Stream = false,
                Messages = messages,
                Temperature = _temperature,
                MaxTokens = _maxTokens,
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{apiUrl}/chat/completions", content, cts.Token);
            var latencyMs = (DateTime.UtcNow - reqTime).TotalMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                _sawmill.Error($"OpenAI backend bad status code: {response.StatusCode}");
                WriteMetric(source, latencyMs, null, jsonFormat, false, 0, ok: false);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            var parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(body);
            var reply = parsed?.Choices is { Count: > 0 } choices ? choices[0].Message?.Content?.Trim() : null;

            if (string.IsNullOrEmpty(reply))
            {
                _sawmill.Error("OpenAI backend returned an empty response");
                WriteMetric(source, latencyMs, null, jsonFormat, false, 0, ok: false);
                return null;
            }

            var jsonValid = !jsonFormat || IsValidJson(reply);
            WriteMetric(source, latencyMs, null, jsonFormat, jsonValid, reply.Length, ok: true);
            _sawmill.Debug($"OpenAI reply in {latencyMs / 1000.0:F1}s ({reply.Length} chars)");
            return reply;
        }
        catch (TaskCanceledException)
        {
            _sawmill.Error($"OpenAI request timed out after {_timeout}s");
            WriteMetric(source, (DateTime.UtcNow - reqTime).TotalMilliseconds, null, jsonFormat, false, 0, ok: false);
            return null;
        }
        catch (Exception e)
        {
            _sawmill.Error($"OpenAI request error\n{e}");
            WriteMetric(source, (DateTime.UtcNow - reqTime).TotalMilliseconds, null, jsonFormat, false, 0, ok: false);
            return null;
        }
    }

    private static bool IsValidJson(string s)
    {
        try
        {
            using var _ = JsonDocument.Parse(s);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Appends one CSV row per model request when saiga.metrics_path is set. Used to gather
    ///     latency / throughput / JSON-validity data for the research log (see SAIGA_RESEARCH.md).
    /// </summary>
    private void WriteMetric(string source, double latencyMs, OllamaChatResponse? r, bool jsonFormat, bool jsonValid, int replyChars, bool ok)
    {
        if (string.IsNullOrEmpty(_metricsPath))
            return;

        try
        {
            var promptTokens = r?.PromptEvalCount ?? 0;
            var evalTokens = r?.EvalCount ?? 0;
            var evalDurationNs = r?.EvalDuration ?? 0;
            var totalMs = (r?.TotalDuration ?? 0) / 1_000_000.0;
            var tokensPerSec = evalDurationNs > 0 ? evalTokens / (evalDurationNs / 1_000_000_000.0) : 0.0;

            var sb = new StringBuilder();
            if (!File.Exists(_metricsPath))
                sb.AppendLine("time_iso,source,model,latency_ms,total_ms,prompt_tokens,eval_tokens,tokens_per_sec,json_format,json_valid,reply_chars,ok");

            sb.Append(DateTime.UtcNow.ToString("o")).Append(',')
                .Append(source).Append(',')
                .Append(_model).Append(',')
                .Append(latencyMs.ToString("F0")).Append(',')
                .Append(totalMs.ToString("F0")).Append(',')
                .Append(promptTokens).Append(',')
                .Append(evalTokens).Append(',')
                .Append(tokensPerSec.ToString("F1")).Append(',')
                .Append(jsonFormat).Append(',')
                .Append(jsonValid).Append(',')
                .Append(replyChars).Append(',')
                .Append(ok).Append('\n');

            lock (_metricsLock)
                File.AppendAllText(_metricsPath, sb.ToString());
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Saiga metrics write failed: {e.Message}");
        }
    }

    // --- DTOs matching the Ollama /api/chat schema ---

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; init; }

        // Keep the model resident so we don't pay cold-load latency between pauses.
        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; init; } = "30m";

        [JsonPropertyName("messages")]
        public IReadOnlyList<SaigaMessage> Messages { get; init; } = new List<SaigaMessage>();

        [JsonPropertyName("options")]
        public OllamaOptions Options { get; init; } = new();
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public float Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; init; }

        [JsonPropertyName("num_gpu")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NumGpu { get; init; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public SaigaMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        // Timing/token stats reported by Ollama (durations in nanoseconds).
        [JsonPropertyName("total_duration")]
        public long TotalDuration { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; init; }

        [JsonPropertyName("eval_count")]
        public int EvalCount { get; init; }

        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; init; }
    }

    // --- DTOs matching the OpenAI /chat/completions schema ---

    private sealed class OpenAiChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("messages")]
        public IReadOnlyList<SaigaMessage> Messages { get; init; } = new List<SaigaMessage>();

        [JsonPropertyName("temperature")]
        public float Temperature { get; init; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; }
    }

    private sealed class OpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAiChoice>? Choices { get; init; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public SaigaMessage? Message { get; init; }
    }
}

/// <summary>
///     A single chat message in the Saiga/Ollama conversation.
/// </summary>
public sealed class SaigaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; }

    [JsonConstructor]
    public SaigaMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
