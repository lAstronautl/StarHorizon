using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._Horizon.Distortion.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Distortion;

/// <summary>
/// Scans for shuttles near <see cref="DistortionFieldComponent"/> sources and glitches
/// out their consoles with static, scaling with proximity - full noise once the shuttle
/// has flown all the way into the field. Optionally also puts the noise on the screens
/// of players aboard the affected shuttle.
/// </summary>
public sealed class DistortionFieldSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const float ScanInterval = 0.5f;
    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < ScanInterval)
            return;
        _accumulator -= ScanInterval;

        var affectedGrids = new Dictionary<EntityUid, (float Intensity, bool AffectPlayers)>();

        var fieldQuery = EntityQueryEnumerator<DistortionFieldComponent, TransformComponent>();
        var fields = new List<(TransformComponent Xform, DistortionFieldComponent Comp)>();
        while (fieldQuery.MoveNext(out _, out var comp, out var xform))
        {
            fields.Add((xform, comp));
        }

        if (fields.Count > 0)
        {
            var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
            while (shuttleQuery.MoveNext(out var gridUid, out _, out var gridXform))
            {
                if (gridXform.MapUid == null)
                    continue;

                var gridAabb = _physics.GetWorldAABB(gridUid);
                var best = 0f;
                var affectPlayers = false;

                foreach (var (fieldXform, field) in fields)
                {
                    if (fieldXform.MapUid != gridXform.MapUid || field.Range <= 0f)
                        continue;

                    var fieldPos = _xform.GetWorldPosition(fieldXform);
                    var closest = Vector2.Clamp(fieldPos, gridAabb.BottomLeft, gridAabb.TopRight);
                    var distance = (fieldPos - closest).Length();

                    var intensity = Math.Clamp(1f - distance / field.Range, 0f, 1f);
                    if (intensity <= 0f)
                        continue;

                    if (intensity > best)
                        best = intensity;

                    if (field.AffectPlayers)
                        affectPlayers = true;
                }

                if (best > 0f)
                    affectedGrids[gridUid] = (best, affectPlayers);
            }
        }

        ApplyGridEffects(affectedGrids);
    }

    private void ApplyGridEffects(Dictionary<EntityUid, (float Intensity, bool AffectPlayers)> affectedGrids)
    {
        var noLongerAffected = new List<EntityUid>();

        var existing = EntityQueryEnumerator<DistortionAffectedComponent>();
        while (existing.MoveNext(out var gridUid, out var affected))
        {
            if (affectedGrids.Remove(gridUid, out var data))
            {
                if (!MathHelper.CloseTo(affected.Intensity, data.Intensity) || affected.AffectPlayers != data.AffectPlayers)
                {
                    affected.Intensity = data.Intensity;
                    affected.AffectPlayers = data.AffectPlayers;
                    Dirty(gridUid, affected);
                }
            }
            else
            {
                noLongerAffected.Add(gridUid);
            }
        }

        foreach (var gridUid in noLongerAffected)
        {
            RemComp<DistortionAffectedComponent>(gridUid);
        }

        foreach (var (gridUid, data) in affectedGrids)
        {
            var affected = EnsureComp<DistortionAffectedComponent>(gridUid);
            affected.Intensity = data.Intensity;
            affected.AffectPlayers = data.AffectPlayers;
            Dirty(gridUid, affected);
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
                if (!MathHelper.CloseTo(vision.Intensity, data.Intensity))
                {
                    vision.Intensity = data.Intensity;
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
