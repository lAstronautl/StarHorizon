
using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness.Systems;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Shitmed.EntityEffects.Effects;

public sealed partial class AdjustConsciousness : EntityEffect
{
    [DataField(required: true)]
    public FixedPoint2 Amount = default!;

    [DataField(required: true)]
    public TimeSpan Time = default!;

    [DataField]
    public string Identifier = "ConsciousnessModifier";

    [DataField]
    public bool AllowNewModifiers = true;

    [DataField]
    public ConsciousnessModType ModifierType = ConsciousnessModType.Generic;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-adjust-consciousness");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        var entMan = args.EntityManager;
        var consciousness = entMan.System<ConsciousnessSystem>();
        if (!consciousness.TryGetNerveSystem(uid, out var nerveSys))
            return;

        var nerves = nerveSys.Value;
        var scale = args is EntityEffectReagentArgs r ? r.Scale : FixedPoint2.New(1);

        // if it fails to edit and isn't allowed to make a new modifier, return
        if (consciousness.EditConsciousnessModifier(uid, nerves, Amount * scale, Identifier, Time)
            || !AllowNewModifiers)
            return;

        consciousness.AddConsciousnessModifier(uid, nerves, Amount * scale, Identifier, ModifierType, Time);
    }
}
