using Content.Server._Horizon.Distortion.Components;
using Content.Shared.Radiation.Events;
using Robust.Shared.Player;

namespace Content.Server._Horizon.Distortion;

/// <summary>
/// Gives players a small amount of the same distortion static while they're taking
/// radiation damage - a much weaker, subtler version of the shuttle distortion field
/// effect meant to read as "you feel sick", not a blinding one. Builds up while
/// irradiated and fades back out once exposure stops.
/// </summary>
public sealed class RadiationDistortionSystem : EntitySystem
{
    // How much exposure one rad of dose adds.
    private const float ExposureGainPerRad = 0.05f;

    // How fast exposure fades once irradiation stops.
    private const float DecayPerSecond = 0.2f;

    // Radiation distortion never gets stronger than this fraction of full intensity.
    private const float MaxIntensity = 0.35f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, OnIrradiatedEvent>(OnIrradiated);
    }

    private void OnIrradiated(EntityUid uid, ActorComponent component, OnIrradiatedEvent args)
    {
        var exposure = EnsureComp<RadiationDistortionComponent>(uid);
        exposure.Exposure = Math.Clamp(exposure.Exposure + args.TotalRads * ExposureGainPerRad, 0f, 1f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var toRemove = new List<EntityUid>();

        var query = EntityQueryEnumerator<RadiationDistortionComponent>();
        while (query.MoveNext(out var uid, out var exposure))
        {
            exposure.Exposure = Math.Max(0f, exposure.Exposure - DecayPerSecond * frameTime);

            if (exposure.Exposure <= 0f)
                toRemove.Add(uid);
        }

        foreach (var uid in toRemove)
        {
            RemComp<RadiationDistortionComponent>(uid);
        }
    }

    /// <summary>
    /// Current radiation-driven distortion intensity for an entity, 0..1 (capped well
    /// below full strength).
    /// </summary>
    public float GetIntensity(EntityUid uid)
    {
        return TryComp<RadiationDistortionComponent>(uid, out var exposure)
            ? exposure.Exposure * MaxIntensity
            : 0f;
    }
}
