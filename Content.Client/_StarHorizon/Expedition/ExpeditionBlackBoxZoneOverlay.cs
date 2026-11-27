using System.Numerics;
using Content.Shared._StarHorizon.Expedition;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Зона чёрного ящика на земле, пока качаются данные: пульсирующее кольцо и полупрозрачная заливка. Оранжевая, когда
/// в зоне есть игрок (скачивание идёт), красная, когда зона пуста (пауза).
/// </summary>
public sealed class ExpeditionBlackBoxZoneOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private static readonly Color Active = Color.FromHex("#F0A030");
    private static readonly Color Empty = Color.FromHex("#FF4A3A");

    public ExpeditionBlackBoxZoneOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var xformSystem = _entMan.System<SharedTransformSystem>();
        var handle = args.WorldHandle;
        var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 3f);

        var query = _entMan.EntityQueryEnumerator<ExpeditionBlackBoxZoneComponent, TransformComponent>();
        while (query.MoveNext(out _, out var zone, out var xform))
        {
            if (!zone.Active || xform.MapID != args.MapId)
                continue;

            var center = xformSystem.GetWorldPosition(xform);
            var color = zone.Occupied ? Active : Empty;
            handle.DrawCircle(center, zone.Radius, color.WithAlpha(0.08f + 0.05f * pulse));
            handle.DrawCircle(center, zone.Radius, color.WithAlpha(0.6f + 0.3f * pulse), filled: false);
            handle.DrawCircle(center, zone.Radius - 0.08f, color.WithAlpha(0.35f), filled: false);
        }
    }
}
