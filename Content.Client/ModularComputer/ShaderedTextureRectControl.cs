using System;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client.ModularComputer;

// Not part of the upstream mod: it extends the OnyxBay14 fork's base UI control of the same
// name, which isn't present here. TextureRect already provides everything ScreenControl needs
// except a convenience for pulling a named ShaderPrototype and configuring its parameters.
public abstract class ShaderedTextureRectControl : TextureRect
{
    [Dependency]
    private readonly IPrototypeManager _prototypeManager = default!;

    protected ShaderedTextureRectControl()
    {
        IoCManager.InjectDependencies(this);
    }

    public void SetShader(string prototypeId, Action<ShaderInstance> configure)
    {
        var shader = _prototypeManager.Index<ShaderPrototype>(prototypeId).InstanceUnique();
        configure(shader);
        ShaderOverride = shader;
    }
}
