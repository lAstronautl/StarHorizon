using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Pain.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Shitmed.EntityEffects.Effects;

// Todo this is half-baked yoinkage from Trauma actually fix this I just don't have the time rn.
public sealed partial class SuppressPain : EntityEffect
{
    [DataField(required: true)]
    public FixedPoint2 Amount = default!;

    [DataField(required: true)]
    public TimeSpan Time = default!;

    [DataField]
    public string ModifierIdentifier = "PainSuppressant";

    /// <summary>
    /// The body part to change the pain for.
    /// </summary>
    [DataField]
    public string OrganCategory = "Chest";

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-pain");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        var entMan = args.EntityManager;
        if (!entMan.TryGetComponent<BodyComponent>(uid, out var body))
            return;

        var consciousness = entMan.System<ConsciousnessSystem>();
        var pain = entMan.System<PainSystem>();
        var bodySys = entMan.System<SharedBodySystem>();
        if (!consciousness.TryGetNerveSystem(uid, out var nerveSys))
            return;

        EntityUid? organ = null;
        foreach (var (id, part) in bodySys.GetBodyChildren(uid, body))
        {
            if (part.PartType.ToString().Equals(OrganCategory, StringComparison.OrdinalIgnoreCase))
            {
                organ = id;
                break;
            }
        }

        if (organ is not { } foundOrgan)
            return;

        var scale = args is EntityEffectReagentArgs r ? r.Scale : FixedPoint2.New(1);
        var nerves = nerveSys.Value;
        var amount = Amount * scale;

        if (pain.TryGetPainModifier(nerves, foundOrgan, ModifierIdentifier, out var modifier))
            pain.TryChangePainModifier(nerves, foundOrgan, ModifierIdentifier, modifier.Value.Change - amount, time: Time);
        else
            pain.TryAddPainModifier(nerves, foundOrgan, ModifierIdentifier, -amount, time: Time);
    }
}
