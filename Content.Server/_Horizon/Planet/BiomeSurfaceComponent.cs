namespace Content.Server._Horizon.Planet;

/// <summary>
/// Метка на сущности биома, рядом с которой есть «скрытая порода» — клетки в глубине массива,
/// где сущность не создавалась. Когда помеченную сущность убирают, соседние скрытые клетки достраиваются.
/// </summary>
[RegisterComponent]
public sealed partial class BiomeSurfaceComponent : Component
{
    [ViewVariables]
    public EntityUid Map;

    [ViewVariables]
    public Vector2i Tile;
}
