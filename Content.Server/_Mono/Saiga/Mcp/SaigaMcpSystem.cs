using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Content.Server.Spawners.Components;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared._Mono.Saiga;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.SubFloor;
using Content.Shared.Tools.Components;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Saiga.Mcp;

/// <summary>
///     Exposes the embodied agent's game tools over a minimal MCP (Model Context Protocol,
///     JSON-RPC 2.0) endpoint at <c>/mcp</c>, so external LLM clients (Claude, GPT, ...) can drive
///     the agent through a standard protocol instead of the built-in keyword resolver.
///
///     The server does NOT execute movement itself: action tools raise the same
///     <see cref="SaigaAgentDecisionResponseEvent"/> that <c>SaigaAgentBrainSystem</c> raises, into
///     the agent's client session — the client performs the action. The external LLM simply takes
///     the place of the deterministic <c>ResolveMovement</c> step.
///
///     Transport mirrors <c>ServerApi</c>: a handler on the existing <see cref="IStatusHost"/>,
///     Bearer auth via <see cref="CryptographicOperations.FixedTimeEquals"/>, and ECS access
///     marshalled onto the main thread via <see cref="ITaskManager"/>.
/// </summary>
public sealed class SaigaMcpSystem : EntitySystem
{
    [Dependency] private readonly IStatusHost _statusHost = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly SaigaManager _saiga = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private ISawmill _sawmill = default!;
    private bool _enabled;
    private string _token = string.Empty;

    private const string ProtocolVersion = "2025-06-18";
    private const float ObserveRange = 10f;
    private const int ObserveMax = 20;
    private const float NearRadius = 2f;   // graph edge: entities within this are "near" each other
    private const int RecallMax = 25;
    private const int RecipesMax = 30;
    private const int HeardMax = 12;   // how many recent overheard lines to keep

    private sealed record McpToolSpec(string Name, string Desc, bool Target, bool TargetRequired, string? TextParam, bool TextRequired);

    // Tool catalogue. Action names mirror SaigaAgentBrainSystem.ResolveMovement's "act" vocabulary.
    private static readonly McpToolSpec[] Specs =
    {
        new("observe",  "Что агент видит вокруг (id, имя, расстояние, направление). filter — опц. имена через запятую, вернуть ТОЛЬКО их (напр. «яблоко,морковь»); пусто = всё. Пишется в память.", false, false, "filter", false),
        new("listen",   "Что агенту сказали вслух рядом (новые реплики с прошлого вызова): кто и что.", false, false, null, false),
        new("say",      "Заставить агента произнести фразу вслух.", false, false, "text", true),
        new("follow",   "Идти за сущностью / подойти к ней (target — её сетевой id из observe).", true, true, null, false),
        new("stop",     "Остановиться, стоять на месте.", false, false, null, false),
        new("pickup",   "Взять предмет в руку (target — сетевой id предмета).", true, true, null, false),
        new("pull",     "Тащить/тянуть предмет (target — сетевой id предмета).", true, true, null, false),
        new("drop",     "Уронить предмет из руки на пол.", false, false, null, false),
        new("swap",     "Сменить активную руку.", false, false, null, false),
        new("store",    "Убрать предмет из руки в сумку (слот back).", false, false, null, false),
        new("throw",    "Бросить предмет из руки в сторону цели (target — сетевой id).", true, true, null, false),
        new("build",    "Построить стену/каркас из стали в руке.", false, false, null, false),
        new("move_to",  "Подойти вплотную к сущности (~0.4м, без взаимодействия). target — сетевой id.", true, true, null, false),
        new("place",    "Положить предмет из руки рядом с целью: подойти вплотную и уронить. target — сетевой id.", true, true, null, false),
        new("recall",   "Вспомнить, что агент видел раньше (память-граф). query — подстрока имени для фильтра (опц.).", false, false, "query", false),
        new("where_is", "Где агент в последний раз видел объект: направление, дистанция, как давно. name — имя объекта.", false, false, "name", true),
        new("recipes",  "Меню крафта: список рецептов сборки (id, имя, тип, материалы скрыты). query — фильтр по имени/категории.", false, false, "query", false),
        new("craft",    "Скрафтить ПРЕДМЕТ по рецепту из материалов в руках/рядом (recipe — id рецепта типа Item из recipes).", false, false, "recipe", true),
        new("construct","Поставить СТРУКТУРУ/раму машины по рецепту в точке агента (recipe — id рецепта типа Structure).", false, false, "recipe", true),
        new("use_on",   "Использовать предмет в руке НА цели (вставить плату/деталь в раму, и т.п.). target — сетевой id.", true, true, null, false),
        new("activate", "Включить/выключить предмет в руке (зажечь сварочник, фонарик; нужно ПЕРЕД сваркой).", false, false, null, false),
    };

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("saiga.mcp");
        _cfg.OnValueChanged(SaigaMcpCVars.Enabled, v => _enabled = v, true);
        _cfg.OnValueChanged(SaigaMcpCVars.Token, v => _token = v, true);
        _statusHost.AddHandler(HandleAsync);

        // Capture overheard speech for the `listen` tool (independent of the C# brain).
        // The brain already owns <SaigaAgentStateComponent, ListenEvent>; subscribe on the
        // listener component instead (one subscriber per comp+event) and filter to our agents.
        SubscribeLocalEvent<ActiveListenerComponent, ListenEvent>(OnHeard);
    }

    private void OnHeard(EntityUid uid, ActiveListenerComponent comp, ListenEvent args)
    {
        if (args.Source == uid || !HasComp<SaigaAgentStateComponent>(uid))
            return; // only our agents, not self
        var text = args.Message.Trim();
        if (text.Length == 0)
            return;

        var hearing = EnsureComp<SaigaHearingComponent>(uid);
        hearing.Lines.Add(new HeardLine
        {
            Net = GetNetEntity(args.Source),
            Speaker = MetaData(args.Source).EntityName,
            Text = text,
            Time = _timing.CurTime,
        });
        while (hearing.Lines.Count > HeardMax)
            hearing.Lines.RemoveAt(0);
    }

    // --- Transport (runs off the main game thread) ---

    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != "/mcp")
            return false; // not ours, let other handlers try

        if (!_enabled || string.IsNullOrEmpty(_token))
        {
            await context.RespondErrorAsync(HttpStatusCode.NotFound);
            return true;
        }

        if (context.RequestMethod != HttpMethod.Post)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        if (!CheckAuth(context))
        {
            await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
            return true;
        }

        JsonElement root;
        try
        {
            root = await context.RequestBodyJsonAsync<JsonElement>();
        }
        catch (Exception)
        {
            await RespondRpcError(context, null, -32700, "Parse error");
            return true;
        }

        await DispatchAsync(context, root);
        return true;
    }

    private bool CheckAuth(IStatusHandlerContext context)
    {
        if (!context.RequestHeaders.TryGetValue("Authorization", out var header))
            return false;

        var value = header.ToString();
        var space = value.IndexOf(' ');
        if (space == -1)
            return false;

        var scheme = value[..space];
        var token = value[space..].Trim();
        if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(_token));
    }

    private async Task DispatchAsync(IStatusHandlerContext context, JsonElement root)
    {
        JsonElement? id = root.TryGetProperty("id", out var idEl) ? idEl : null;

        if (!root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
        {
            await RespondRpcError(context, id, -32600, "Invalid Request: missing method");
            return;
        }

        var method = methodEl.GetString()!;
        switch (method)
        {
            case "initialize":
                await RespondRpcResult(context, id, BuildInitialize(root));
                break;
            case "notifications/initialized":
            case "notifications/cancelled":
                await context.RespondNoContentAsync();
                break;
            case "ping":
                await RespondRpcResult(context, id, new JsonObject());
                break;
            case "tools/list":
                await RespondRpcResult(context, id, BuildToolsList());
                break;
            case "tools/call":
                await HandleToolCall(context, id, root);
                break;
            default:
                await RespondRpcError(context, id, -32601, $"Method not found: {method}");
                break;
        }
    }

    private static JsonNode BuildInitialize(JsonElement root)
    {
        var version = ProtocolVersion;
        if (root.TryGetProperty("params", out var p)
            && p.TryGetProperty("protocolVersion", out var pv)
            && pv.ValueKind == JsonValueKind.String)
        {
            version = pv.GetString()!;
        }

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "saiga-agent-mcp", ["version"] = "0.1.0" },
        };
    }

    private static JsonNode BuildToolsList()
    {
        var tools = new JsonArray();
        foreach (var s in Specs)
        {
            var props = new JsonObject
            {
                ["agent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Сетевой id (число) или имя сущности-агента.",
                },
            };
            var required = new JsonArray { "agent" };

            if (s.Target)
            {
                props["target"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Сетевой id целевой сущности (из observe).",
                };
                if (s.TargetRequired)
                    required.Add("target");
            }

            if (s.TextParam is { } tp)
            {
                var desc = tp switch
                {
                    "query" => "Подстрока имени/категории для фильтра (опционально).",
                    "filter" => "Имена через запятую — вернуть только их (напр. яблоко,морковь). Пусто = всё.",
                    "name" => "Имя объекта для поиска в памяти агента.",
                    "recipe" => "Id рецепта сборки (из тула recipes).",
                    _ => "Текст реплики.",
                };
                props[tp] = new JsonObject { ["type"] = "string", ["description"] = desc };
                if (s.TextRequired)
                    required.Add(tp);
            }

            tools.Add(new JsonObject
            {
                ["name"] = s.Name,
                ["description"] = s.Desc,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = required,
                },
            });
        }

        return new JsonObject { ["tools"] = tools };
    }

    private async Task HandleToolCall(IStatusHandlerContext context, JsonElement? id, JsonElement root)
    {
        if (!root.TryGetProperty("params", out var p)
            || !p.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
        {
            await RespondRpcError(context, id, -32602, "Invalid params: missing tool name");
            return;
        }

        var name = nameEl.GetString()!;
        if (Specs.All(s => s.Name != name))
        {
            await RespondRpcError(context, id, -32602, $"Unknown tool: {name}");
            return;
        }

        var args = p.TryGetProperty("arguments", out var a) ? a : default;

        McpToolResult result;
        try
        {
            result = await RunOnMainThread(() => ExecuteTool(name, args));
        }
        catch (Exception e)
        {
            _sawmill.Warning($"MCP tool '{name}' threw: {e}");
            result = McpToolResult.Error($"internal error: {e.Message}");
        }

        await RespondRpcResult(context, id, new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.Text } },
            ["isError"] = result.IsError,
        });
    }

    // --- Tool execution (runs ON the main game thread) ---

    private McpToolResult ExecuteTool(string name, JsonElement args)
    {
        if (!TryResolveAgent(args, out var agent, out var session, out var agentErr))
            return McpToolResult.Error(agentErr);

        if (name == "observe")
            return Observe(agent, args);
        if (name == "listen")
            return Listen(agent);
        if (name == "recall")
            return Recall(agent, args);
        if (name == "where_is")
            return WhereIs(agent, args);
        if (name == "recipes")
            return Recipes(args);

        string act;
        NetEntity? target = null;
        string? say = null;
        string? arg = null;

        switch (name)
        {
            case "craft":
                if (!TryGetString(args, "recipe", out arg) || string.IsNullOrWhiteSpace(arg))
                    return McpToolResult.Error("укажи 'recipe' (id рецепта из recipes)");
                if (!IsRecipe(arg, ConstructionType.Item, out var cErr))
                    return McpToolResult.Error(cErr);
                act = "craft";
                break;
            case "construct":
                if (!TryGetString(args, "recipe", out arg) || string.IsNullOrWhiteSpace(arg))
                    return McpToolResult.Error("укажи 'recipe' (id рецепта из recipes)");
                if (!IsRecipe(arg, ConstructionType.Structure, out var coErr))
                    return McpToolResult.Error(coErr);
                act = "construct";
                break;
            case "use_on":
                if (!TryResolveTarget(args, out target, out var uErr))
                    return McpToolResult.Error(uErr);
                act = "use_on";
                break;
            case "activate":
                act = "activate";
                break;
            case "say":
                if (!TryGetString(args, "text", out say) || string.IsNullOrWhiteSpace(say))
                    return McpToolResult.Error("параметр 'text' обязателен");
                act = "none";
                break;
            case "stop":
                act = "stop";
                break;
            case "drop":
                act = "drop";
                break;
            case "swap":
                act = "swap";
                break;
            case "build":
                act = "build";
                break;
            case "store":
                if (!_inventory.TryGetSlotEntity(agent, "back", out var bag))
                    return McpToolResult.Error("у агента нет сумки в слоте back");
                target = GetNetEntity(bag.Value);
                act = "store";
                break;
            case "follow":
                if (!TryResolveTarget(args, out target, out var fErr))
                    return McpToolResult.Error(fErr);
                act = "follow";
                break;
            case "pickup":
                if (!TryResolveTarget(args, out target, out var pErr))
                    return McpToolResult.Error(pErr);
                act = "pickup";
                break;
            case "pull":
                if (!TryResolveTarget(args, out target, out var lErr))
                    return McpToolResult.Error(lErr);
                act = "pull";
                break;
            case "throw":
                if (!TryResolveTarget(args, out target, out var tErr))
                    return McpToolResult.Error(tErr);
                act = "throw";
                break;
            case "move_to":
                if (!TryResolveTarget(args, out target, out var mErr))
                    return McpToolResult.Error(mErr);
                act = "move_to";
                break;
            case "place":
                if (!TryResolveTarget(args, out target, out var plErr))
                    return McpToolResult.Error(plErr);
                act = "place";
                break;
            default:
                return McpToolResult.Error($"unknown tool: {name}");
        }

        RaiseNetworkEvent(new SaigaAgentDecisionResponseEvent(say, act, target, arg), session);
        _saiga.LogTranscript("mcp", MetaData(agent).EntityName, null, say, act, target?.ToString() ?? arg);

        var detail = target is { } tn ? $" target={tn.Id}" : "";
        var argInfo = arg != null ? $" recipe={arg}" : "";
        var said = say != null ? $" say=\"{say}\"" : "";
        return McpToolResult.Ok($"ok: act={act}{detail}{argInfo}{said}");
    }

    private McpToolResult Observe(EntityUid self, JsonElement args)
    {
        var selfMap = _xform.GetMapCoordinates(self);
        var found = new List<Seen>();

        foreach (var ent in _lookup.GetEntitiesInRange(self, ObserveRange))
        {
            if (ent == self)
                continue;
            if (_container.IsEntityInContainer(ent))
                continue;
            if (HasComp<SubFloorHideComponent>(ent))
                continue;
            if (HasComp<AudioComponent>(ent))
                continue;
            // Invisible map markers / spawners — players don't see these (mirror SaigaAgentBrainSystem.GetNearby).
            if (HasComp<GhostRoleMobSpawnerComponent>(ent)
                || HasComp<ConditionalSpawnerComponent>(ent)
                || HasComp<RandomSpawnerComponent>(ent)
                || HasComp<TimedSpawnerComponent>(ent))
                continue;
            if (!TryComp<MetaDataComponent>(ent, out var meta) || string.IsNullOrWhiteSpace(meta.EntityName))
                continue;

            var pos = _xform.GetMapCoordinates(ent);
            if (pos.MapId != selfMap.MapId)
                continue;
            if (!_examine.InRangeUnOccluded(self, ent, ObserveRange))
                continue;

            var delta = pos.Position - selfMap.Position;
            found.Add(new Seen(GetNetEntity(ent), meta.EntityName, Category(ent), ToolQualities(ent),
                pos.Position, pos.MapId, delta.Length(), delta));
        }

        RecordMemory(self, found); // memory graph always gets the FULL perception

        // Optional name filter: model can call observe(filter="яблоко,морковь") to see ONLY those —
        // keeps the result small (no junk, no context overflow) and focused on what it needs.
        TryGetString(args, "filter", out var filter);
        var terms = string.IsNullOrWhiteSpace(filter)
            ? null
            : filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant()).ToArray();

        var shownList = terms == null
            ? found
            : found.Where(f => terms.Any(t => f.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();

        if (shownList.Count == 0)
            return McpToolResult.Ok(terms == null
                ? "Рядом ничего не видно."
                : $"По фильтру «{filter}» рядом ничего не видно.");

        shownList.Sort((x, y) => x.Dist.CompareTo(y.Dist));

        var sb = new StringBuilder();
        sb.Append(terms == null
            ? "Вижу рядом (id, имя, расстояние, направление):\n"
            : $"По фильтру «{filter}» (id, имя, расстояние, направление):\n");
        var shown = 0;
        foreach (var f in shownList)
        {
            if (shown++ >= ObserveMax)
                break;
            var tool = f.Tool.Length > 0 ? $" [инстр:{f.Tool}]" : "";
            sb.Append($"- id={f.Net.Id} {f.Name}{tool} ({f.Dist:F1}м, {DirText(f.Delta)})\n");
        }

        return McpToolResult.Ok(sb.ToString().TrimEnd());
    }

    private McpToolResult Listen(EntityUid self)
    {
        if (!TryComp<SaigaHearingComponent>(self, out var hearing) || hearing.Lines.Count == 0)
            return McpToolResult.Ok("Пока ничего не слышал.");

        var unread = hearing.Lines.Where(l => !l.Read).ToList();
        foreach (var l in hearing.Lines)
            l.Read = true;

        if (unread.Count == 0)
            return McpToolResult.Ok("Новых реплик нет.");

        var now = _timing.CurTime;
        var sb = new StringBuilder();
        sb.Append("Тебе сказали (id говорящего, кто: что, как давно):\n");
        foreach (var l in unread)
        {
            var age = (int)(now - l.Time).TotalSeconds;
            sb.Append($"- id={l.Net.Id} {l.Speaker}: «{l.Text}» ({age}с назад)\n");
        }
        return McpToolResult.Ok(sb.ToString().TrimEnd());
    }

    private readonly record struct Seen(NetEntity Net, string Name, string Cat, string Tool, Vector2 Pos, MapId MapId, float Dist, Vector2 Delta);

    private string Category(EntityUid e)
        => HasComp<ActorComponent>(e) ? "персонаж"
            : HasComp<MobStateComponent>(e) ? "существо"
            : HasComp<ToolComponent>(e) ? "инструмент"
            : HasComp<ItemComponent>(e) ? "предмет"
            : "объект";

    /// <summary>Tool qualities (Prying/Anchoring/Screwing/Cutting/Welding/...) or "" if not a tool.</summary>
    private string ToolQualities(EntityUid e)
    {
        if (!TryComp<ToolComponent>(e, out var tool) || tool.HideQualities)
            return string.Empty;
        return string.Join(",", tool.Qualities);
    }

    /// <summary>Upserts seen entities into the agent's memory graph and refreshes their "near" edges.</summary>
    private void RecordMemory(EntityUid self, List<Seen> found)
    {
        if (found.Count == 0)
            return;

        var mem = EnsureComp<SaigaMemoryComponent>(self);
        var now = _timing.CurTime;

        foreach (var f in found)
        {
            if (!mem.Nodes.TryGetValue(f.Net, out var node))
            {
                node = new MemNode { Net = f.Net };
                mem.Nodes[f.Net] = node;
            }

            node.Name = f.Name;
            node.Category = f.Cat;
            node.Tool = f.Tool;
            node.Pos = f.Pos;
            node.MapId = f.MapId;
            node.LastSeen = now;

            node.Near.Clear();
            foreach (var g in found)
            {
                if (g.Net != f.Net && (g.Pos - f.Pos).Length() <= NearRadius)
                    node.Near.Add(g.Net);
            }
        }
    }

    private McpToolResult Recall(EntityUid self, JsonElement args)
    {
        if (!TryComp<SaigaMemoryComponent>(self, out var mem) || mem.Nodes.Count == 0)
            return McpToolResult.Ok("Память пуста — я ещё ничего не запомнил. Вызови observe.");

        TryGetString(args, "query", out var query);
        var now = _timing.CurTime;

        var nodes = mem.Nodes.Values
            .Where(n => string.IsNullOrEmpty(query) || n.Name.Contains(query!, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => (now - n.LastSeen))
            .Take(RecallMax)
            .ToList();

        if (nodes.Count == 0)
            return McpToolResult.Ok($"В памяти нет ничего по запросу «{query}».");

        var sb = new StringBuilder();
        sb.Append(string.IsNullOrEmpty(query)
            ? $"Помню {mem.Nodes.Count} объект(ов):\n"
            : $"По «{query}» помню:\n");
        foreach (var n in nodes)
        {
            var age = (int)(now - n.LastSeen).TotalSeconds;
            var near = NearNames(mem, n);
            var tool = n.Tool.Length > 0 ? $" (инстр:{n.Tool})" : "";
            sb.Append($"- id={n.Net.Id} {n.Name}{tool} [{n.Category}], видел {age}с назад{(near.Length > 0 ? $", рядом: {near}" : "")}\n");
        }
        return McpToolResult.Ok(sb.ToString().TrimEnd());
    }

    private McpToolResult WhereIs(EntityUid self, JsonElement args)
    {
        if (!TryGetString(args, "name", out var name) || string.IsNullOrWhiteSpace(name))
            return McpToolResult.Error("укажи параметр 'name'");
        if (!TryComp<SaigaMemoryComponent>(self, out var mem) || mem.Nodes.Count == 0)
            return McpToolResult.Ok("Память пуста — ничего не помню.");

        var now = _timing.CurTime;
        var match = mem.Nodes.Values
            .Where(n => n.Name.Contains(name!, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => (now - n.LastSeen))
            .FirstOrDefault();

        if (match == null)
            return McpToolResult.Ok($"Не помню, где «{name}» — не видел такого.");

        var age = (int)(now - match.LastSeen).TotalSeconds;
        var selfMap = _xform.GetMapCoordinates(self);
        string place;
        if (selfMap.MapId == match.MapId)
        {
            var delta = match.Pos - selfMap.Position;
            place = $"~{delta.Length():F1}м на {DirText(delta)}";
        }
        else
        {
            place = "на другой карте";
        }

        var near = NearNames(mem, match);
        return McpToolResult.Ok(
            $"{match.Name} [{match.Category}]: {place}, видел {age}с назад{(near.Length > 0 ? $". Рядом было: {near}" : "")}.");
    }

    private static string NearNames(SaigaMemoryComponent mem, MemNode node)
    {
        var names = new List<string>();
        foreach (var net in node.Near)
        {
            if (mem.Nodes.TryGetValue(net, out var n))
                names.Add(n.Name);
            if (names.Count >= 4)
                break;
        }
        return string.Join(", ", names);
    }

    private McpToolResult Recipes(JsonElement args)
    {
        TryGetString(args, "query", out var q);

        var list = new List<ConstructionPrototype>();
        foreach (var p in _proto.EnumeratePrototypes<ConstructionPrototype>())
        {
            if (p.Hide)
                continue;
            if (!string.IsNullOrEmpty(q)
                && (p.Name is null || !p.Name.Contains(q!, StringComparison.OrdinalIgnoreCase))
                && !p.ID.Contains(q!, StringComparison.OrdinalIgnoreCase)
                && !p.Category.Contains(q!, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(p);
        }

        if (list.Count == 0)
            return McpToolResult.Ok($"Рецептов по запросу «{q}» не найдено.");

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.Append($"Рецепты ({Math.Min(list.Count, RecipesMax)}/{list.Count}):\n");
        var n = 0;
        foreach (var p in list)
        {
            if (n++ >= RecipesMax)
                break;
            var type = p.Type == ConstructionType.Item ? "предмет" : "структура";
            var cat = string.IsNullOrEmpty(p.Category) ? "" : ", " + p.Category;
            sb.Append($"- id={p.ID} «{p.Name}» [{type}{cat}]\n");
        }
        if (list.Count > RecipesMax)
            sb.Append($"… ещё {list.Count - RecipesMax}. Уточни query.");

        return McpToolResult.Ok(sb.ToString().TrimEnd());
    }

    private bool IsRecipe(string id, ConstructionType type, out string error)
    {
        error = string.Empty;
        if (!_proto.TryIndex<ConstructionPrototype>(id, out var p))
        {
            error = $"нет рецепта с id={id} — глянь recipes";
            return false;
        }
        if (p.Type != type)
        {
            error = type == ConstructionType.Item
                ? $"«{id}» — структура, используй construct, не craft"
                : $"«{id}» — предмет, используй craft, не construct";
            return false;
        }
        return true;
    }

    // --- Resolution helpers (main thread) ---

    private bool TryResolveAgent(JsonElement args, out EntityUid agent, out ICommonSession session, out string error)
    {
        agent = default;
        session = default!;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("agent", out var a))
        {
            error = "параметр 'agent' обязателен";
            return false;
        }

        EntityUid? uid = null;
        if (a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var idn))
            uid = ResolveNet(idn);
        else if (a.ValueKind == JsonValueKind.String)
        {
            var s = a.GetString()!;
            uid = int.TryParse(s, out var ids) ? ResolveNet(ids) : FindAgentByName(s);
        }

        if (uid is not { } u || !Exists(u))
        {
            error = "агент не найден";
            return false;
        }

        if (!TryComp<ActorComponent>(u, out var actor))
        {
            error = "у сущности нет игрока (ActorComponent) — это не управляемый агент";
            return false;
        }

        agent = u;
        session = actor.PlayerSession;
        return true;
    }

    private bool TryResolveTarget(JsonElement args, out NetEntity? target, out string error)
    {
        target = null;
        error = string.Empty;

        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("target", out var t))
        {
            error = "параметр 'target' обязателен";
            return false;
        }

        int id;
        if (t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out id))
        {
        }
        else if (t.ValueKind == JsonValueKind.String && int.TryParse(t.GetString(), out id))
        {
        }
        else
        {
            error = "'target' должен быть сетевым id (число)";
            return false;
        }

        var net = new NetEntity(id);
        if (!TryGetEntity(net, out _))
        {
            error = $"целевая сущность {id} не найдена";
            return false;
        }

        target = net;
        return true;
    }

    private EntityUid? ResolveNet(int id)
        => TryGetEntity(new NetEntity(id), out var uid) ? uid : null;

    private EntityUid? FindAgentByName(string name)
    {
        var query = EntityQueryEnumerator<SaigaAgentStateComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (string.Equals(MetaData(uid).EntityName, name, StringComparison.OrdinalIgnoreCase))
                return uid;
        }
        return null;
    }

    private static bool TryGetString(JsonElement args, string prop, out string? value)
    {
        value = null;
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(prop, out var v)
            && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        return false;
    }

    private static string DirText(Vector2 d)
    {
        if (d.LengthSquared() < 0.01f)
            return "здесь";

        // +X = восток, +Y = север. 8-way compass.
        var ang = (MathF.Atan2(d.Y, d.X) * 180f / MathF.PI + 360f) % 360f;
        string[] names = { "В", "СВ", "С", "СЗ", "З", "ЮЗ", "Ю", "ЮВ" };
        return names[(int)MathF.Round(ang / 45f) % 8];
    }

    // --- JSON-RPC response helpers ---

    private static async Task RespondRpcResult(IStatusHandlerContext context, JsonElement? id, JsonNode result)
    {
        await context.RespondJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = IdNode(id),
            ["result"] = result,
        });
    }

    private static async Task RespondRpcError(IStatusHandlerContext context, JsonElement? id, int code, string message)
    {
        await context.RespondJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = IdNode(id),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });
    }

    private static JsonNode? IdNode(JsonElement? id)
        => id is { } e && e.ValueKind != JsonValueKind.Null && e.ValueKind != JsonValueKind.Undefined
            ? JsonNode.Parse(e.GetRawText())
            : null;

    private async Task<T> RunOnMainThread<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        });
        return await tcs.Task;
    }

    private readonly record struct McpToolResult(string Text, bool IsError)
    {
        public static McpToolResult Ok(string text) => new(text, false);
        public static McpToolResult Error(string text) => new(text, true);
    }
}
