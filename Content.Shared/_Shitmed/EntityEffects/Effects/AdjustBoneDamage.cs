// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared.Body;
using Content.Shared.EntityEffects;
using System.Linq;
using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Components;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Shitmed.EntityEffects.Effects;

/// <summary>
/// Evenly deals bone damage to each bone in the target mob.
/// The damage is split between them.
/// </summary>
public sealed partial class AdjustBoneDamage : EntityEffect
{
    [DataField(required: true)]
    public FixedPoint2 Amount = default!;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-adjust-bone-damage", ("amount", Amount));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var uid = args.TargetEntity;
        var entMan = args.EntityManager;
        if (!entMan.TryGetComponent<BodyComponent>(uid, out var body))
            return;

        var bodySys = entMan.System<SharedBodySystem>();
        var trauma = entMan.System<TraumaSystem>();
        var parts = bodySys.GetBodyChildrenWithComponent<WoundableComponent>(uid, body).ToList();
        if (parts.Count == 0)
            return;

        var amount = Amount / parts.Count;
        foreach (var (_, _, woundable) in parts)
        {
            var bone = woundable.Bone.ContainedEntities.FirstOrNull();
            if (bone == null || !entMan.TryGetComponent<BoneComponent>(bone, out var boneComp))
                continue;

            trauma.ApplyDamageToBone(bone.Value, amount, boneComp);
        }
    }
}
