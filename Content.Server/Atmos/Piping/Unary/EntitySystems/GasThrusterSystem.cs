using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Unary.EntitySystems;

/// <summary>
/// Drives <see cref="GasThrusterComponent"/>: reads the pressure of the attached pipe network and translates it
/// into thrust on the entity's <see cref="ThrusterComponent"/>, venting gas out of the pipe while the thruster
/// is actually firing.
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
        var range = component.MaxPressure - component.MinPressure;
        var ratio = range > 0f
            ? Math.Clamp((pressure - component.MinPressure) / range, 0f, 1f)
            : (pressure >= component.MaxPressure ? 1f : 0f);

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
