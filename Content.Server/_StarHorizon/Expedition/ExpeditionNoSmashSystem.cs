using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Salvage.Expeditions;
using Robust.Shared.Timing;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// На картах экспедиций мобы не ломают стены: у всех ИИ выключается NavSmash. Иначе путь прокладывается сквозь
/// стену, и моб (ксенос, бот АнКо) долбит её вместо обхода. Проверка раз в секунду, поэтому покрывает и мобов
/// данжа, и появившихся позже (волны, биом).
/// </summary>
public sealed class ExpeditionNoSmashSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var expeditions = GetEntityQuery<SalvageExpeditionComponent>();
        var query = EntityQueryEnumerator<HTNComponent, TransformComponent>();
        while (query.MoveNext(out _, out var htn, out var xform))
        {
            if (xform.MapUid is not { } map || !expeditions.HasComponent(map))
                continue;

            DisableSmash(htn);
        }
    }

    /// <summary>Выключить ломание стен у ИИ (если оно было включено).</summary>
    public void DisableSmash(HTNComponent htn)
    {
        if (htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavSmash, out var smash, EntityManager) && smash)
            htn.Blackboard.SetValue(NPCBlackboard.NavSmash, false);
    }
}
