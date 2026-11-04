using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>Чёрная виньетка, пока у локального игрока есть <see cref="DropPodVignetteComponent"/>.</summary>
public sealed class DropPodVignetteSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private DropPodVignetteOverlay? _instance;

    public override void Initialize()
    {
        base.Initialize();

        _instance = new DropPodVignetteOverlay();
        _overlay.AddOverlay(_instance);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_instance != null)
            _overlay.RemoveOverlay(_instance);
    }
}

/// <summary>
/// Чёрное кольцо, сужающееся к центру, как тёмный слой крита в <c>DamageOverlay</c> (без белой каймы).
/// Уровень берётся из <see cref="DropPodVignetteComponent"/> по времени.
/// </summary>
public sealed class DropPodVignetteOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _darkShader;

    private float _level;

    public DropPodVignetteOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 1000;
        _darkShader = _proto.Index(CircleMaskShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _level = 0f;

        if (_player.LocalEntity is not { } player ||
            !_entMan.TryGetComponent(player, out DropPodVignetteComponent? vignette) ||
            !_entMan.TryGetComponent(player, out EyeComponent? eye) ||
            args.Viewport.Eye != eye.Eye)
            return false;

        var duration = (vignette.End - vignette.Start).TotalSeconds;
        var t = duration <= 0 ? 1f : (float) Math.Clamp((_timing.CurTime - vignette.Start).TotalSeconds / duration, 0, 1);
        _level = Math.Clamp(vignette.From + (vignette.To - vignette.From) * t, 0f, 1f);

        return _level > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var viewport = args.WorldAABB;
        var distance = args.ViewportBounds.Width;
        var level = _level;

        // Чёрное кольцо.
        {
            var outerMax = 0.6f * distance;
            var outerMin = 0.06f * distance;
            var innerRadius = 0.02f * distance;
            var outerRadius = outerMax - level * (outerMax - outerMin);
            // К краям экрана - полностью чёрный.
            var darkness = Math.Clamp(level * 2f, 0f, 1f);

            _darkShader.SetParameter("time", 0.0f);
            _darkShader.SetParameter("color", new Vector3(0f, 0f, 0f));
            _darkShader.SetParameter("darknessAlphaOuter", darkness);
            _darkShader.SetParameter("innerCircleRadius", innerRadius);
            _darkShader.SetParameter("innerCircleMaxRadius", innerRadius);
            _darkShader.SetParameter("outerCircleRadius", outerRadius);
            _darkShader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
            handle.UseShader(_darkShader);
            handle.DrawRect(viewport, Color.White);
        }

        handle.UseShader(null);
    }
}
