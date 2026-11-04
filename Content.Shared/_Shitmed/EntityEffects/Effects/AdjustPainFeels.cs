using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Pain.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Shitmed.EntityEffects.Effects;

public sealed partial class AdjustPainFeels : EntityEffect
{
    [DataField(required: true)]
    public FixedPoint2 Amount = default!;

    [DataField]
    public string ModifierIdentifier = "PainSuppressant";

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-pain", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        var entMan = args.EntityManager;
        if (!entMan.TryGetComponent<BodyComponent>(uid, out var body))
            return;

        var consciousness = entMan.System<ConsciousnessSystem>();
        var pain = entMan.System<PainSystem>();
        var bodySys = entMan.System<SharedBodySystem>();
        var random = IoCManager.Resolve<IRobustRandom>();
        if (!consciousness.TryGetNerveSystem(uid, out var nerveSys))
            return;

        var scale = args is EntityEffectReagentArgs r ? r.Scale : FixedPoint2.New(1);
        var nerves = nerveSys.Value;
        var amount = Amount * scale;
        foreach (var (bodyPart, _) in bodySys.GetBodyChildren(uid, body))
        {
            var add = random.Prob(0.3f) ? amount : -amount;
            if (pain.TryGetPainFeelsModifier(bodyPart, nerves, ModifierIdentifier, out _))
                pain.TryChangePainFeelsModifier(nerves, ModifierIdentifier, bodyPart, add);
            else
                pain.TryAddPainFeelsModifier(nerves, ModifierIdentifier, bodyPart, add);
        }
    }
}
