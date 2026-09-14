using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._Horizon.Distortion.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Horizon.Distortion;

/// <summary>
/// Scans for shuttles and radar/mass-scanner consoles near <see cref="DistortionFieldComponent"/>
/// sources and glitches them out with static, scaling with proximity - full noise once a shuttle
/// has flown all the way into the field, or once a handheld scanner is right on top of it.
/// Optionally also puts the noise on the screens of players aboard an affected shuttle.
/// </summary>
public sealed class DistortionFieldSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const float ScanInterval = 0.5f;

    // The faint edge-of-screen warning ring reaches 1.5x farther than the main effect.
    private const float OuterRangeMultiplier = 1.5f;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < ScanInterval)
            return;
        _accumulator -= ScanInterval;

        var fieldQuery = EntityQueryEnumerator<DistortionFieldComponent, TransformComponent>();
        var fields = new List<(TransformComponent Xform, DistortionFieldComponent Comp)>();
        while (fieldQuery.MoveNext(out _, out var comp, out var xform))
        {
            fields.Add((xform, comp));
        }

        var affectedEntities = new Dictionary<EntityUid, (float Intensity, float OuterIntensity, bool AffectPlayers)>();

        if (fields.Count > 0)
        {
            ScanShuttleGrids(fields, affectedEntities);
            ScanRadarConsoles(fields, affectedEntities);
        }

        ApplyEffects(affectedEntities);
    }

    // Whole-ship coverage: intensity is based on the field's distance to the shuttle's hull,
    // so the entire ship glitches out once the field is anywhere inside it.
    private void ScanShuttleGrids(
        List<(TransformComponent Xform, DistortionFieldComponent Comp)> fields,
        Dictionary<EntityUid, (float Intensity, float OuterIntensity, bool AffectPlayers)> affectedEntities)
    {
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapUid == null)
                continue;

            var gridAabb = _physics.GetWorldAABB(gridUid);
            var best = 0f;
            var bestOuter = 0f;
            var affectPlayers = false;

            foreach (var (fieldXform, field) in fields)
            {
                if (fieldXform.MapUid != gridXform.MapUid || field.Range <= 0f)
                    continue;

                var fieldPos = _xform.GetWorldPosition(fieldXform);
                var closest = Vector2.Clamp(fieldPos, gridAabb.BottomLeft, gridAabb.TopRight);
                var distance = (fieldPos - closest).Length();

                var intensity = Math.Clamp(1f - distance / field.Range, 0f, 1f);
                var outerIntensity = Math.Clamp(1f - distance / (field.Range * OuterRangeMultiplier), 0f, 1f);

                if (intensity > best)
                    best = intensity;

                if (outerIntensity > bestOuter)
                    bestOuter = outerIntensity;

                if (intensity > 0f && field.AffectPlayers)
                    affectPlayers = true;
            }

            if (best > 0f || bestOuter > 0f)
                MergeBest(affectedEntities, gridUid, best, bestOuter, affectPlayers);
        }
    }

    // Per-device coverage: also glitches out standalone radar consoles and handheld mass
    // scanners based on their own exact position, so a mass scanner picks up interference
    // just from being carried near a field, without needing to be aboard a shuttle.
    private void ScanRadarConsoles(
        List<(TransformComponent Xform, DistortionFieldComponent Comp)> fields,
        Dictionary<EntityUid, (float Intensity, float OuterIntensity, bool AffectPlayers)> affectedEntities)
    {
        var consoleQuery = EntityQueryEnumerator<RadarConsoleComponent, TransformComponent>();
        while (consoleQuery.MoveNext(out var consoleUid, out _, out var consoleXform))
        {
            if (consoleXform.MapUid == null)
                continue;

            var consolePos = _xform.GetWorldPosition(consoleXform);
            var best = 0f;
            var bestOuter = 0f;
            var affectPlayers = false;

            foreach (var (fieldXform, field) in fields)
            {
                if (fieldXform.MapUid != consoleXform.MapUid || field.Range <= 0f)
                    continue;

                var fieldPos = _xform.GetWorldPosition(fieldXform);
                var distance = (fieldPos - consolePos).Length();

                var intensity = Math.Clamp(1f - distance / field.Range, 0f, 1f);
                var outerIntensity = Math.Clamp(1f - distance / (field.Range * OuterRangeMultiplier), 0f, 1f);

                if (intensity > best)
                    best = intensity;

                if (outerIntensity > bestOuter)
                    bestOuter = outerIntensity;

                if (intensity > 0f && field.AffectPlayers)
                    affectPlayers = true;
            }

            if (best > 0f || bestOuter > 0f)
                MergeBest(affectedEntities, consoleUid, best, bestOuter, affectPlayers);
        }
    }

    private static void MergeBest(
        Dictionary<EntityUid, (float Intensity, float OuterIntensity, bool AffectPlayers)> affectedEntities,
        EntityUid uid,
        float intensity,
        float outerIntensity,
        bool affectPlayers)
    {
        if (affectedEntities.TryGetValue(uid, out var existing))
        {
            affectedEntities[uid] = (
                Math.Max(existing.Intensity, intensity),
                Math.Max(existing.OuterIntensity, outerIntensity),
                existing.AffectPlayers || affectPlayers);
        }
        else
        {
            affectedEntities[uid] = (intensity, outerIntensity, affectPlayers);
        }
    }

    private void ApplyEffects(Dictionary<EntityUid, (float Intensity, float OuterIntensity, bool AffectPlayers)> affectedEntities)
    {
        var noLongerAffected = new List<EntityUid>();

        var existing = EntityQueryEnumerator<DistortionAffectedComponent>();
        while (existing.MoveNext(out var uid, out var affected))
        {
            if (affectedEntities.Remove(uid, out var data))
            {
                if (!MathHelper.CloseTo(affected.Intensity, data.Intensity) ||
                    !MathHelper.CloseTo(affected.OuterIntensity, data.OuterIntensity) ||
                    affected.AffectPlayers != data.AffectPlayers)
                {
                    affected.Intensity = data.Intensity;
                    affected.OuterIntensity = data.OuterIntensity;
                    affected.AffectPlayers = data.AffectPlayers;
                    Dirty(uid, affected);
                }
            }
            else
            {
                noLongerAffected.Add(uid);
            }
        }

        foreach (var uid in noLongerAffected)
        {
            RemComp<DistortionAffectedComponent>(uid);
        }

        foreach (var (uid, data) in affectedEntities)
        {
            var affected = EnsureComp<DistortionAffectedComponent>(uid);
            affected.Intensity = data.Intensity;
            affected.OuterIntensity = data.OuterIntensity;
            affected.AffectPlayers = data.AffectPlayers;
            Dirty(uid, affected);
        }

        UpdatePlayerVision();
    }

    private void UpdatePlayerVision()
    {
        var affected = GetEntityQuery<DistortionAffectedComponent>();
        var mobQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (mobQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } gridUid &&
                affected.TryGetComponent(gridUid, out var data) &&
                data.AffectPlayers)
            {
                var vision = EnsureComp<DistortionVisionComponent>(uid);
                if (!MathHelper.CloseTo(vision.Intensity, data.Intensity) ||
                    !MathHelper.CloseTo(vision.OuterIntensity, data.OuterIntensity))
                {
                    vision.Intensity = data.Intensity;
                    vision.OuterIntensity = data.OuterIntensity;
                    Dirty(uid, vision);
                }
            }
            else if (HasComp<DistortionVisionComponent>(uid))
            {
                RemComp<DistortionVisionComponent>(uid);
            }
        }
    }
}
