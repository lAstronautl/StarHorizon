using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>Шлейф за падающей капсулой: длинная полоса, которая плавно исчезает за <see cref="Lifetime"/>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodTrailComponent : Component
{
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(2);
}
