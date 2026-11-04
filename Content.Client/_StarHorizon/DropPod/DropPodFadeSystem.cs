using Content.Shared._StarHorizon.DropPod;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.DropPod;

/// <summary>Чёрный оверлей на весь экран, пока у локального игрока есть <see cref="DropPodFadeComponent"/>.</summary>
public sealed class DropPodFadeSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private DropPodFadeOverlay? _instance;

    public override void Initialize()
    {
        base.Initialize();

        _instance = new DropPodFadeOverlay();
        _overlay.AddOverlay(_instance);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_instance != null)
            _overlay.RemoveOverlay(_instance);
    }
}

public sealed class DropPodFadeOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // Поверх мира и света, но под интерфейсом.
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private float _alpha;

    public DropPodFadeOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 1000;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _alpha = 0f;

        if (_player.LocalEntity is not { } player ||
            !_entMan.TryGetComponent(player, out DropPodFadeComponent? fade))
            return false;

        var duration = (fade.End - fade.Start).TotalSeconds;
        var t = duration <= 0 ? 1f : (float) Math.Clamp((_timing.CurTime - fade.Start).TotalSeconds / duration, 0, 1);
        _alpha = Math.Clamp(fade.From + (fade.To - fade.From) * t, 0f, 1f);

        return _alpha > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        args.WorldHandle.DrawRect(args.WorldBounds, Color.Black.WithAlpha(_alpha));
    }
}
