using System.Numerics;
using Content.Shared._Horizon.SpaceHazards;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client._Horizon.Shuttles;

/// <summary>
/// Border rings around stars and black holes for the shuttle console radar and map, like the ones the sector
/// map shows: a black hole gets its pull, hazard and event horizon borders, a star gets the border of the zone
/// where solar panels work better.
/// </summary>
public static class SectorBodyRings
{
    private static readonly Color Pull = Color.FromHex("#A040FF").WithAlpha(0.45f);
    private static readonly Color Hazard = Color.FromHex("#D080FF").WithAlpha(0.55f);
    private static readonly Color Horizon = Color.FromHex("#F5F540").WithAlpha(0.7f);
    private static readonly Color SolarBoost = Color.FromHex("#7CFF8A").WithAlpha(0.5f);
    private static readonly Color StarHazard = Color.FromHex("#FFB020").WithAlpha(0.5f);

    /// <param name="toScreen">Converts a world position on the viewed map to control pixels.</param>
    /// <param name="pixelsPerMeter">Control pixels per world meter.</param>
    public static void Draw(
        DrawingHandleScreen handle,
        IEntityManager entMan,
        SharedTransformSystem xformSys,
        MapId mapId,
        Func<Vector2, Vector2> toScreen,
        float pixelsPerMeter,
        UIBox2 bounds)
    {
        if (mapId == MapId.Nullspace)
            return;

        var query = entMan.AllEntityQueryEnumerator<SectorCelestialBodyComponent, TransformComponent>();
        while (query.MoveNext(out _, out var body, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var center = toScreen(xformSys.GetWorldPosition(xform));

            if (body.Kind == CelestialKind.BlackHole)
            {
                Ring(handle, center, body.PullRadius, pixelsPerMeter, bounds, Pull);
                Ring(handle, center, body.HazardRadius, pixelsPerMeter, bounds, Hazard);
                Ring(handle, center, body.EventHorizonRadius, pixelsPerMeter, bounds, Horizon);
            }
            else
            {
                if (!body.Harmless)
                    Ring(handle, center, body.HazardRadius, pixelsPerMeter, bounds, StarHazard);

                Ring(handle, center, body.SolarBoostRadius, pixelsPerMeter, bounds, SolarBoost);
            }
        }
    }

    private static void Ring(DrawingHandleScreen handle, Vector2 center, float radiusMeters, float pixelsPerMeter, UIBox2 bounds, Color color)
    {
        if (radiusMeters <= 1f)
            return;

        var radius = radiusMeters * pixelsPerMeter;

        // Only draw rings whose line passes through the visible area (huge off-screen circles are wasted work).
        var boundsCenter = bounds.Center;
        var halfDiagonal = (bounds.BottomRight - bounds.TopLeft).Length() * 0.5f;
        if (MathF.Abs(Vector2.Distance(center, boundsCenter) - radius) > halfDiagonal)
            return;

        handle.DrawCircle(center, radius, color, filled: false);
    }
}
