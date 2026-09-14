using Content.Shared._Horizon.Distortion.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Horizon.Distortion;

/// <summary>
/// Draws a vignette of static around the edges of the screen for a player standing on a
/// shuttle caught in a distortion field that has its <c>AffectPlayers</c> option enabled.
/// Stays semi-transparent and leaves a clear patch in the middle so the player can still
/// see something ahead of them, even at full intensity.
/// </summary>
public sealed class DistortionVisionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> DistortionStaticShader = "DistortionStatic";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;
    private float _intensity;

    public DistortionVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(DistortionStaticShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var playerEntity = _playerManager.LocalSession?.AttachedEntity;

        if (!_entityManager.TryGetComponent(playerEntity, out DistortionVisionComponent? vision) || vision.Intensity <= 0f)
            return false;

        _intensity = vision.Intensity;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        _shader.SetParameter("Intensity", _intensity);
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldBounds, Color.White);
        worldHandle.UseShader(null);
    }
}
