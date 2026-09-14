using Content.Shared._Horizon.Distortion.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Horizon.Distortion.UI;

/// <summary>
/// Transparent overlay control that glitches out a shuttle console's screen with
/// static, scaling with how close the shuttle's grid currently is to a
/// distortion field. Place it as the topmost layer over a console window's contents.
/// </summary>
public sealed class DistortionStaticControl : Control
{
    private static readonly ProtoId<ShaderPrototype> CameraStaticShader = "CameraStatic";

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly ShaderInstance _shader;

    /// <summary>
    /// The console entity whose grid should be checked for interference.
    /// </summary>
    public EntityUid? Console;

    public DistortionStaticControl()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(CameraStaticShader).Instance();
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var intensity = GetIntensity();
        if (intensity <= 0f)
            return;

        handle.UseShader(_shader);
        handle.DrawRect(PixelSizeBox, Color.White.WithAlpha(intensity));
        handle.UseShader(null);
    }

    private float GetIntensity()
    {
        if (Console is not { } console || !_entManager.EntityExists(console))
            return 0f;

        if (!_entManager.TryGetComponent(console, out TransformComponent? xform) || xform.GridUid is not { } grid)
            return 0f;

        return _entManager.TryGetComponent(grid, out DistortionAffectedComponent? affected)
            ? affected.Intensity
            : 0f;
    }
}
