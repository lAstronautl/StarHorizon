using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>Клуб дыма дроп-пода: растёт от <see cref="StartScale"/> до <see cref="EndScale"/> и тает за <see cref="Lifetime"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodSmokePuffComponent : Component
{
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(2.5);

    [DataField]
    public float StartScale = 0.35f;

    [DataField]
    public float EndScale = 1.1f;
}
