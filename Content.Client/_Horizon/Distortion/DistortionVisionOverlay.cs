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
/// see something ahead of them, even at full intensity. Layered with a fainter, farther-
/// reaching ring confined to the very edges of the screen as an early warning, and the
/// Cataracts shader (same hazy, swimmy distortion used for blurry vision and the
/// underwater effect) up close, for a disorienting, drug-like wobble.
/// </summary>
public sealed class DistortionVisionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> DistortionStaticShader = "DistortionStatic";
    private static readonly ProtoId<ShaderPrototype> CataractsShader = "Cataracts";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _innerShader;
    private readonly ShaderInstance _outerShader;
    private readonly ShaderInstance _cataractsShader;
    private float _intensity;
    private float _outerIntensity;

    // Caps how strong the cataracts distortion/cloudiness gets at Intensity = 1.
    private const float MaxWarpStrength = 0.18f;

    public DistortionVisionOverlay()
    {
        IoCManager.InjectDependencies(this);

        // Draw after other vision-affecting overlays (blurry vision, drunk, night vision,
        // the AI eye, etc. all default to 0) so the cataracts pass warps whatever they've
        // already drawn too, instead of getting drawn over and hidden by them.
        ZIndex = 20;

        _innerShader = _prototypeManager.Index(DistortionStaticShader).InstanceUnique();

        _outerShader = _prototypeManager.Index(DistortionStaticShader).InstanceUnique();
        _outerShader.SetParameter("MinClearRadius", 0.75f);
        _outerShader.SetParameter("MaxClearRadius", 1.3f);
        _outerShader.SetParameter("EdgeWidth", 0.25f);
        _outerShader.SetParameter("MaxAlpha", 0.45f);

        _cataractsShader = _prototypeManager.Index(CataractsShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var playerEntity = _playerManager.LocalSession?.AttachedEntity;

        if (!_entityManager.TryGetComponent(playerEntity, out DistortionVisionComponent? vision) ||
            (vision.Intensity <= 0f && vision.OuterIntensity <= 0f))
        {
            return false;
        }

        _intensity = vision.Intensity;
        _outerIntensity = vision.OuterIntensity;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var worldHandle = args.WorldHandle;
        var playerEntity = _playerManager.LocalSession?.AttachedEntity;

        if (_intensity > 0f)
        {
            var zoom = 1.0f;
            if (_entityManager.TryGetComponent<EyeComponent>(playerEntity, out var eyeComponent))
                zoom = eyeComponent.Zoom.X;

            var strength = _intensity * MaxWarpStrength;

            _cataractsShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            _cataractsShader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
            _cataractsShader.SetParameter("Zoom", zoom);
            _cataractsShader.SetParameter("DistortionScalar", MathF.Pow(strength, 2f));
            _cataractsShader.SetParameter("CloudinessScalar", MathF.Pow(strength, 2f));

            worldHandle.UseShader(_cataractsShader);
            worldHandle.DrawRect(args.WorldBounds, Color.White);
        }

        if (_outerIntensity > 0f)
        {
            _outerShader.SetParameter("Intensity", _outerIntensity);
            worldHandle.UseShader(_outerShader);
            worldHandle.DrawRect(args.WorldBounds, Color.White);
        }

        if (_intensity > 0f)
        {
            _innerShader.SetParameter("Intensity", _intensity);
            worldHandle.UseShader(_innerShader);
            worldHandle.DrawRect(args.WorldBounds, Color.White);
        }

        worldHandle.UseShader(null);
    }
}
