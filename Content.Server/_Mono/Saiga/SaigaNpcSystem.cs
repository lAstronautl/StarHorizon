using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared.Chat;
using Content.Shared.Mind.Components;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Saiga;

/// <summary>
///     Drives <see cref="SaigaNpcComponent"/>: listens to nearby speech and replies with the
///     local Saiga (Ollama) model. The model also picks a body action (follow/stop), which is
///     executed by the game's native NPC AI (HTN) — so movement runs on CPU, the GPU is only
///     used to understand the request.
/// </summary>
public sealed class SaigaNpcSystem : EntitySystem
{
    [Dependency] private readonly SaigaManager _saiga = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private ISawmill _sawmill = default!;

    // Appended so the model returns a structured reply with an optional body action.
    private const string ActionContract =
        "Кроме реплики ты можешь управлять телом. Отвечай СТРОГО одним JSON-объектом без пояснений: " +
        "{\"say\": <реплика по-русски или null>, \"action\": <\"follow\"|\"stop\"|\"none\">}. " +
        "action=follow — пойти за тем, кто к тебе обратился, НО только если он явно просит идти/следовать за ним. " +
        "action=stop — остановиться и стоять на месте, если просят остаться/подождать. " +
        "action=none — ничего телом не делать (по умолчанию). Никогда не двигайся без явной просьбы.";

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("saiga.npc");

        SubscribeLocalEvent<SaigaNpcComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SaigaNpcComponent, ListenEvent>(OnListen);
    }

    private void OnStartup(EntityUid uid, SaigaNpcComponent comp, ComponentStartup args)
    {
        // ActiveListenerComponent is what actually routes nearby speech to us via ListenEvent.
        var listener = EnsureComp<ActiveListenerComponent>(uid);
        listener.Range = comp.Range;
    }

    private async void OnListen(EntityUid uid, SaigaNpcComponent comp, ListenEvent args)
    {
        if (!_saiga.Enabled)
            return;

        if (args.Source == uid)
            return; // don't react to our own voice

        if (comp.Busy)
            return; // already thinking about a previous line

        if (_timing.CurTime < comp.NextResponse)
            return; // on cooldown

        // If a real player is controlling this entity, stay silent and let them talk.
        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            return;

        // Avoid two Saiga NPCs talking to each other forever.
        if (HasComp<SaigaNpcComponent>(args.Source))
            return;

        if (comp.RespondToPlayersOnly && !HasComp<ActorComponent>(args.Source))
            return;

        var message = args.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return;

        // Reserve the slot and start the cooldown now.
        comp.Busy = true;
        comp.NextResponse = _timing.CurTime + TimeSpan.FromSeconds(comp.Cooldown);

        try
        {
            var messages = new List<SaigaMessage>
            {
                new("system", comp.SystemPrompt),
                new("system", ActionContract),
            };
            messages.AddRange(comp.History);
            messages.Add(new("user", message));

            // Continuation resumes on the main game thread (RobustSynchronizationContext).
            var reply = await _saiga.ChatAsync(messages, jsonFormat: true, source: "npc");

            if (reply == null || TerminatingOrDeleted(uid))
                return;

            string? say;
            string? action;
            try
            {
                var parsed = JsonSerializer.Deserialize<NpcAction>(reply);
                say = parsed?.Say;
                action = parsed?.Action;
            }
            catch (JsonException)
            {
                _sawmill.Warning($"could not parse NPC JSON: {reply}");
                return;
            }

            // Update conversation memory and trim it.
            comp.History.Add(new("user", message));
            if (!string.IsNullOrWhiteSpace(say))
                comp.History.Add(new("assistant", say!));
            var max = comp.MaxHistory * 2;
            if (comp.History.Count > max)
                comp.History.RemoveRange(0, comp.History.Count - max);

            ApplyAction(uid, args.Source, action);

            _saiga.LogTranscript("npc", MetaData(args.Source).EntityName, message, say, action);

            if (!string.IsNullOrWhiteSpace(say))
            {
                _chat.TrySendInGameICMessage(
                    uid,
                    say!,
                    comp.Whisper ? InGameICChatType.Whisper : InGameICChatType.Speak,
                    hideChat: false,
                    hideLog: false,
                    checkRadioPrefix: false);
            }
        }
        finally
        {
            comp.Busy = false;
        }
    }

    /// <summary>
    ///     Executes the model's body action through the native NPC AI. Safe no-op for entities
    ///     without an HTN brain (e.g. the pAI device).
    /// </summary>
    private void ApplyAction(EntityUid uid, EntityUid speaker, string? action)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "follow":
                // EntityCoordinates relative to the speaker auto-track them as they move.
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(speaker, Vector2.Zero));
                break;

            case "stop":
            case "stay":
                if (TryComp<HTNComponent>(uid, out var htn))
                    htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
                break;
        }
    }

    private sealed class NpcAction
    {
        [JsonPropertyName("say")] public string? Say { get; set; }
        [JsonPropertyName("action")] public string? Action { get; set; }
    }
}
