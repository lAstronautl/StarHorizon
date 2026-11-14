namespace Content.Server._Horizon.Planet;

/// <summary>
/// Постоянная зона биома: чанки в радиусе от центра генерируются один раз заранее и не выгружаются.
/// За пределами радиуса биом работает как обычно (подгрузка за игроком).
/// </summary>
[RegisterComponent]
public sealed partial class BiomePreloadComponent : Component
{
    /// <summary>
    /// Радиус зоны в клетках.
    /// </summary>
    [DataField]
    public int Radius;

    /// <summary>
    /// Центр зоны в клетках карты.
    /// </summary>
    [DataField]
    public Vector2i Center;

    /// <summary>
    /// Начала чанков зоны, от центра наружу.
    /// </summary>
    [ViewVariables]
    public List<Vector2i> Queue = new();

    [ViewVariables]
    public int NextIndex;

    [ViewVariables]
    public bool Completed;

    /// <summary>
    /// Суммарное время, потраченное на прогрузку.
    /// </summary>
    [ViewVariables]
    public TimeSpan Spent;
}
