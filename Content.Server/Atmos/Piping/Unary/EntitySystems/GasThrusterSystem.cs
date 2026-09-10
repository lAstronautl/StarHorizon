using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems;

/// <summary>
/// Drives <see cref="GasThrusterComponent"/>: reads the pressure of the attached pipe network and translates it
/// into thrust on the entity's <see cref="ThrusterComponent"/>, venting gas out of the pipe while the thruster
/// is actually firing. Both thrust and gas consumption scale together with the same pressure ratio, so a
/// higher-pressure feed makes the engine both stronger and hungrier.
/// </summary>
[UsedImplicitly]
public sealed class GasThrusterSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasThrusterComponent, AtmosDeviceUpdateEvent>(OnGasThrusterUpdated);
        SubscribeLocalEvent<GasThrusterComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// How far along the Min-Max pressure band the given pressure sits, clamped to [0, 1]. This ratio drives
    /// both the thrust output and the gas consumption rate, keeping them tied together.
    /// </summary>
    private float GetPressureRatio(GasThrusterComponent component, float pressure)
    {
        var range = component.MaxPressure - component.MinPressure;

        return range > 0f
            ? Math.Clamp((pressure - component.MinPressure) / range, 0f, 1f)
            : (pressure >= component.MaxPressure ? 1f : 0f);
    }

    private void OnExamined(EntityUid uid, GasThrusterComponent component, ExaminedEvent args)
    {
        if (!_nodeContainer.TryGetNode(uid, component.InletName, out PipeNode? inlet))
            return;

        var pressure = inlet.Air.Pressure;
        var ratio = GetPressureRatio(component, pressure);

        using (args.PushGroup(nameof(GasThrusterComponent)))
        {
            args.PushMarkup(Loc.GetString("gas-thruster-comp-pressure", ("pressure", (int) pressure)));
            args.PushMarkup(Loc.GetString("gas-thruster-comp-thrust", ("percent", (int) MathF.Round(ratio * 100f))));
        }
    }

    private void OnGasThrusterUpdated(EntityUid uid, GasThrusterComponent component, ref AtmosDeviceUpdateEvent args)
    {
        if (!TryComp<ThrusterComponent>(uid, out var thruster))
            return;

        if (!_nodeContainer.TryGetNode(uid, component.InletName, out PipeNode? inlet))
        {
            _thruster.SetThrust(uid, thruster, 0f);
            return;
        }

        var pressure = inlet.Air.Pressure;
        var ratio = GetPressureRatio(component, pressure);

        _thruster.SetThrust(uid, thruster, component.MaxThrust * ratio);

        // Only actually vent (and consume) gas while the engine is firing under thrust, not just idling on standby.
        if (ratio <= 0f || !_thruster.IsFiring(thruster))
            return;

        var environment = _atmosphereSystem.GetContainingMixture(uid, args.Grid, args.Map, true, true);

        if (environment == null)
            return;

        var volume = MathF.Max(inlet.Air.Volume, 1f);
        var transferRatio = MathF.Min(1f, args.dt * component.MaxTransferRate * ratio / volume);
        var removed = inlet.Air.RemoveRatio(transferRatio);

        _atmosphereSystem.Merge(environment, removed);
    }
}
