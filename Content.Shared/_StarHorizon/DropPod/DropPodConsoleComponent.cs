using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Консоль управления дроп-подами. Привязывается к ближайшей капсуле.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodConsoleComponent : Component
{
    /// <summary>Радиус поиска капсулы.</summary>
    [DataField]
    public float LinkRange = 6f;

    [ViewVariables]
    public EntityUid? LinkedPod;

    /// <summary>Максимальное расстояние до цели от начала координат карты.</summary>
    [DataField]
    public float MaxCoordinate = 20000f;
}
