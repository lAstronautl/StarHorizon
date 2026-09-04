using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server.Spawners.Components;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared._Mono.Saiga;
using Content.Shared.Examine;
using Content.Shared.Ghost.Roles.Components;
using Content.Shared.Inventory;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.SubFloor;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Saiga;

/// <summary>
///     Server-side "brain" for the client AI agent. Reacts event-driven: whenever the agent's
///     character hears a player speak, it builds context, asks the local Saiga model for a
///     structured reply, decides movement deterministically (keywords + object matching), and
///     pushes the result to that client. Movement itself is executed client-side.
/// </summary>
public sealed class SaigaAgentBrainSystem : EntitySystem
{
    [Dependency] private readonly SaigaManager _saiga = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    private ISawmill _sawmill = default!;

    private const float PerceptionRange = 10f;
    private const float HearingRange = 12f;
    private const int MaxHistory = 8;
    private const int MaxListed = 14;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1.5);

    private const string SystemPrompt =
        "Ты — ИИ, управляющий персонажем в Space Station 14. Отвечай коротко и естественно, по-русски. " +
        "Что ты умеешь по просьбе: идти за человеком, подойти к предмету, взять предмет в руку, " +
        "тащить предмет, уронить его, бросить, убрать в рюкзак, сменить руку, осмотреться вокруг. " +
        "Если просят что-то из этого — спокойно соглашайся, действие выполнится само; не отказывайся, что «не можешь». " +
        "Главное правило: ничего не выдумывай — не сочиняй факты, события и предметы, которых нет. " +
        "Если спрашивают, где что-то находится, или просят подойти к чему-то, чего нет в списке окружения — " +
        "честно скажи, что не видишь этого рядом. Не придумывай местоположения. " +
        "Если чего-то не знаешь — честно скажи, что не знаешь. " +
        "Отвечай строго одним JSON-объектом: {\"say\": <реплика по-русски>}.";

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("saiga.agent");
        SubscribeNetworkEvent<SaigaAgentEnableEvent>(OnEnable);
        SubscribeLocalEvent<SaigaAgentStateComponent, ListenEvent>(OnHeard);
    }

    private void OnEnable(SaigaAgentEnableEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } self || Deleted(self))
            return;

        if (!ev.Enabled)
        {
            if (TryComp<SaigaAgentStateComponent>(self, out var existing))
                existing.AgentEnabled = false;
            return;
        }

        var state = EnsureComp<SaigaAgentStateComponent>(self);
        state.AgentEnabled = true;
        state.Goal = ev.Goal;

        // Per-player inference: route this player's requests to THEIR Ollama, if the server allows it.
        if (_cfg.GetCVar(SaigaCVars.AllowClientEndpoint))
        {
            state.Endpoint = ev.Endpoint;
            state.NumGpu = ev.NumGpu;
        }
        else
        {
            state.Endpoint = "";
            state.NumGpu = -1;
        }

        var listener = EnsureComp<ActiveListenerComponent>(self);
        listener.Range = HearingRange;

        _sawmill.Info($"agent enabled for {ToPrettyString(self)} goal=\"{ev.Goal}\" endpoint=\"{state.Endpoint}\" num_gpu={state.NumGpu}");
    }

    private void OnHeard(EntityUid uid, SaigaAgentStateComponent state, ListenEvent args)
    {
        if (!state.AgentEnabled || !_saiga.Enabled)
            return;
        if (args.Source == uid)
            return;
        if (!HasComp<ActorComponent>(args.Source)) // only react to real players
            return;
        if (state.Busy || _timing.CurTime < state.NextResponse)
            return;
        if (!TryComp<ActorComponent>(uid, out var actor))
            return; // not a client-controlled character

        var message = args.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return;

        state.LastSpeaker = args.Source;
        state.Busy = true;
        state.NextResponse = _timing.CurTime + Cooldown;

        _ = DecideAsync(uid, state, actor.PlayerSession, MetaData(args.Source).EntityName, message);
    }

    private async Task DecideAsync(EntityUid self, SaigaAgentStateComponent state, ICommonSession session, string speaker, string message)
    {
        try
        {
            var heard = $"{speaker}: {message}";

            var goal = state.Goal.Trim();
            if (goal.Length == 0 || goal.All(char.IsDigit))
                goal = "Ты обычный член экипажа. Спокойно общайся с тем, кто к тебе обращается.";

            // Ground truth: what the game actually sees around the character.
            var nearby = GetNearby(self);
            var perceptionText = DescribePerception(nearby);

            // Raw log: distinct names with counts, nearest first (e.g. "стена×8; стол×3; мазь×2").
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var n in nearby)
            {
                if (!counts.ContainsKey(n.Name))
                    order.Add(n.Name);
                counts[n.Name] = counts.GetValueOrDefault(n.Name) + 1;
            }
            var perceptionRaw = order.Count == 0
                ? "(пусто)"
                : string.Join("; ", order.Select(nm => counts[nm] > 1 ? $"{nm}×{counts[nm]}" : nm));

            string? say;

            if (IsLookQuery(message))
            {
                // "Что видишь?" — отвечаем списком восприятия напрямую: мгновенно, без LLM,
                // без галлюцинаций (это и был самый медленный кейс — 200+ токенов).
                say = nearby.Count == 0
                    ? "Рядом ничего особенного не вижу."
                    : "Вижу: " + DescribePerception(nearby);
            }
            else
            {
                var messages = new List<SaigaMessage> { new("system", SystemPrompt) };
                messages.AddRange(state.History);
                messages.Add(new("user",
                    $"Тебя зовут {MetaData(self).EntityName}.\nЦель: {goal}\nТебе сказали: {heard}\nОкружение: {perceptionText}\nТвоя реплика (только JSON):"));

                var action = await RequestActionAsync(messages, state);
                if (Deleted(self))
                    return;

                if (action != null && string.IsNullOrWhiteSpace(action.Say))
                {
                    messages.Add(new("user",
                        "Твой ответ был пустым. Обязательно дай короткую НЕпустую реплику по-русски в том же JSON."));
                    var retry = await RequestActionAsync(messages, state);
                    if (Deleted(self))
                        return;
                    if (retry != null && !string.IsNullOrWhiteSpace(retry.Say))
                        action = retry;
                }

                say = action?.Say;
                if (string.IsNullOrWhiteSpace(say))
                    return; // nothing meaningful to say
            }

            // Action decided deterministically (NOT by the model).
            var (act, target) = ResolveMovement(self, state, message, nearby);

            // --- Перцепционная прослойка в логах: что реально видит игра ---
            _sawmill.Info($"perception=[{perceptionRaw}]");
            _sawmill.Info($"heard=[{heard}] -> say=\"{say}\" act={act} target={target}");
            _saiga.LogTranscript("agent", MetaData(self).EntityName, heard, say, act, target?.ToString(), perceptionRaw);

            state.History.Add(new("user", heard));
            if (!string.IsNullOrWhiteSpace(say))
                state.History.Add(new("assistant", say!));
            while (state.History.Count > MaxHistory)
                state.History.RemoveAt(0);

            RaiseNetworkEvent(new SaigaAgentDecisionResponseEvent(say, act, target), session);
        }
        finally
        {
            state.Busy = false;
        }
    }

    /// <summary>
    ///     Decides movement from the spoken text (reliable, not via the model):
    ///     stop / follow the speaker / approach a named nearby object.
    /// </summary>
    private (string Act, NetEntity? Target) ResolveMovement(EntityUid self, SaigaAgentStateComponent state, string message, List<NearbyEnt> nearby)
    {
        var t = message.ToLowerInvariant();

        bool Has(params string[] words) => words.Any(w => t.Contains(w));

        if (Has("стой", "стоп", "останов", "останься", "оставайся", "подожди", "жди", "не ходи", "не иди", "стоять", "на месте"))
            return ("stop", null);

        // Build a wall/girder where the player points (Stage 1: places the girder with held steel).
        if (Has("построй стен", "построй здесь стен", "собери стен", "поставь стен", "возведи стен", "построй гирдер", "поставь каркас"))
            return ("build", null);

        // Put the held item into her backpack.
        if (Has("в сумк", "в рюкзак", "убери", "спрячь"))
        {
            if (_inventory.TryGetSlotEntity(self, "back", out var bag))
                return ("store", GetNetEntity(bag.Value));
        }

        // Swap active hand.
        if (Has("смени рук", "поменяй рук", "другую рук", "другая рука", "переключи рук"))
            return ("swap", null);

        // Pick up a named object: "возьми мазь".
        if (Has("возьми", "подними", "подбери", "захвати", "в руку"))
        {
            if (FindApproachTarget(t, nearby) is { } o)
                return ("pickup", GetNetEntity(o));
        }

        // Pull/drag a named object: "тащи ящик", "тяни".
        if (Has("тащи", "тяни", "волоки", "потяни", "тащить"))
        {
            if (FindApproachTarget(t, nearby) is { } o)
                return ("pull", GetNetEntity(o));
        }

        // Throw the held item toward whoever asked.
        if (Has("кинь", "швырни", "метни", "брось"))
        {
            if (state.LastSpeaker is { } sp && !Deleted(sp))
                return ("throw", GetNetEntity(sp));
        }

        // Drop the held item on the floor.
        if (Has("урони", "выброс", "выкинь", "опусти", "на пол", "положи на"))
            return ("drop", null);

        // Follow the speaker.
        if (Has("иди за", "идти за", "за мной", "следуй", "следом", "пойдём", "пойдем", "пошли", "идём", "идем", "прогул", "веди меня", "за мно"))
        {
            if (state.LastSpeaker is { } sp && !Deleted(sp))
                return ("follow", GetNetEntity(sp));
        }

        // Approach a named object.
        if (Has("подойд", "подойти", "иди к", "идти к", "топай к", "двигай к", "встань у", "встань рядом"))
        {
            if (FindApproachTarget(t, nearby) is { } o)
                return ("follow", GetNetEntity(o));
        }

        return ("none", null);
    }

    private static bool IsLookQuery(string message)
    {
        var t = message.ToLowerInvariant();
        string[] kw =
        {
            "что видишь", "что ты видишь", "что вокруг", "что рядом", "осмотр", "оглян", "огляд",
            "что заметил", "опиши окруж", "перечисли", "что видно", "что там",
        };
        return kw.Any(k => t.Contains(k));
    }

    /// <summary>Finds the nearby entity whose name best matches a word in the message (declension-tolerant).</summary>
    private static EntityUid? FindApproachTarget(string loweredMessage, List<NearbyEnt> nearby)
    {
        EntityUid? best = null;
        var bestScore = 0;

        foreach (var ent in nearby)
        {
            foreach (var word in ent.Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 4)
                    continue;
                // Match on a declension-tolerant prefix: "мазь"->"маз" finds "мази",
                // "холодильник"->"холодильн" finds "холодильнику".
                var stem = word[..Math.Max(3, word.Length - 2)];
                if (loweredMessage.Contains(stem) && stem.Length > bestScore)
                {
                    bestScore = stem.Length;
                    best = ent.Uid;
                }
            }
        }

        return best;
    }

    private async Task<AgentAction?> RequestActionAsync(IReadOnlyList<SaigaMessage> messages, SaigaAgentStateComponent state)
    {
        var reply = await _saiga.ChatAsync(messages, jsonFormat: true, source: "agent",
            endpoint: state.Endpoint, numGpuOverride: state.NumGpu);
        if (reply == null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<AgentAction>(reply);
        }
        catch (JsonException)
        {
            _sawmill.Warning($"could not parse agent JSON: {reply}");
            return null;
        }
    }

    private List<NearbyEnt> GetNearby(EntityUid self)
    {
        var result = new List<NearbyEnt>();
        var selfPos = _xform.GetMapCoordinates(self);

        foreach (var ent in _lookup.GetEntitiesInRange(self, PerceptionRange))
        {
            if (ent == self)
                continue;

            // Nested entities (her organs/implants, items in bags/pockets/hands, reagents).
            if (_container.IsEntityInContainer(ent))
                continue;

            // Under-floor stuff she can't see: cables, pipes, disposal.
            if (HasComp<SubFloorHideComponent>(ent))
                continue;

            // Transient sound-effect entities ("Audio (...)").
            if (HasComp<AudioComponent>(ent))
                continue;

            // Invisible map markers / spawners — players don't see these.
            if (HasComp<GhostRoleMobSpawnerComponent>(ent)
                || HasComp<ConditionalSpawnerComponent>(ent)
                || HasComp<RandomSpawnerComponent>(ent)
                || HasComp<TimedSpawnerComponent>(ent))
                continue;

            if (!TryComp<MetaDataComponent>(ent, out var meta) || string.IsNullOrWhiteSpace(meta.EntityName))
                continue;

            var pos = _xform.GetMapCoordinates(ent);
            if (pos.MapId != selfPos.MapId)
                continue;

            // Don't see through walls — require an unoccluded line of sight.
            if (!_examine.InRangeUnOccluded(self, ent, PerceptionRange))
                continue;

            result.Add(new NearbyEnt(ent, meta.EntityName, pos.Position - selfPos.Position));
        }

        // Closest first, so the listed/most-relevant ones win.
        result.Sort((a, b) => a.Delta.LengthSquared().CompareTo(b.Delta.LengthSquared()));
        return result;
    }

    /// <summary>Distinct object names, nearest first, capped — no coordinates (concise, AI-style).</summary>
    private static string DescribePerception(List<NearbyEnt> nearby)
    {
        if (nearby.Count == 0)
            return "рядом никого и ничего нет.";

        var seen = new HashSet<string>();
        var names = new List<string>();
        foreach (var n in nearby) // nearby is sorted nearest-first
        {
            if (seen.Add(n.Name))
                names.Add(n.Name);
            if (names.Count >= MaxListed)
                break;
        }

        return string.Join(", ", names) + ".";
    }

    private readonly record struct NearbyEnt(EntityUid Uid, string Name, Vector2 Delta);

    private sealed class AgentAction
    {
        [JsonPropertyName("say")] public string? Say { get; set; }
    }
}
