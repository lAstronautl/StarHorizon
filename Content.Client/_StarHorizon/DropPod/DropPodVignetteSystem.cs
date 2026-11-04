using System.Numerics;
using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>Виньетка как у человека в крите, пока у локального игрока есть <see cref="DropPodVignetteComponent"/>.</summary>
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
/// Те же два слоя, что и у крита в <c>DamageOverlay</c>: тёмное кольцо, сужающееся к центру,
/// и белая пульсирующая кайма. Уровень берётся из <see cref="DropPodVignetteComponent"/> по времени.
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
    private readonly ShaderInstance _critShader;

    private float _level;

    public DropPodVignetteOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 1000;
        _darkShader = _proto.Index(CircleMaskShader).InstanceUnique();
        _critShader = _proto.Index(CircleMaskShader).InstanceUnique();
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
        var time = (float) _timing.RealTime.TotalSeconds;
        var level = _level;

        // Тёмное кольцо (кислородное голодание / крит в DamageOverlay).
        {
            var outerMax = 0.6f * distance;
            var outerMin = 0.06f * distance;
            var innerRadius = 0.02f * distance;
            var outerRadius = outerMax - level * (outerMax - outerMin);
            var darkness = MathF.Min(0.98f, MathF.Max(0f, 0.3f * MathF.Log(level) + 1f));

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

        // Белая пульсирующая кайма крита.
        {
            var outerMax = 2.0f * distance;
            var outerMin = 1.0f * distance;
            var innerMax = 0.6f * distance;
            var innerMin = 0.02f * distance;
            var outerRadius = outerMax - level * (outerMax - outerMin);
            var innerRadius = innerMax - level * (innerMax - innerMin);

            _critShader.SetParameter("time", MathF.Max(0f, MathF.Sin(time)));
            _critShader.SetParameter("color", new Vector3(1f, 1f, 1f));
            _critShader.SetParameter("darknessAlphaOuter", 1.0f);
            _critShader.SetParameter("innerCircleRadius", innerRadius);
            _critShader.SetParameter("innerCircleMaxRadius", innerRadius + 0.005f * distance);
            _critShader.SetParameter("outerCircleRadius", outerRadius);
            _critShader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
            handle.UseShader(_critShader);
            handle.DrawRect(viewport, Color.White);
        }

        handle.UseShader(null);
    }
}
