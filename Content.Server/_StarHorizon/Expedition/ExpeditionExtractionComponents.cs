using System.Numerics;

namespace Content.Server._StarHorizon.Expedition;

public enum ExtractionStage : byte
{
    None,
    BeaconPlaced,
    ShuttleSent,
    Departed,
}

/// <summary>
/// Эвакуация с планетной экспедиции. Висит на карте экспедиции: за <see cref="BeaconBefore"/> до конца ставится маяк
/// и крюки, за <see cref="ArriveBefore"/> прилетает alleycat, за <see cref="DepartBefore"/> он забирает маяк и всех,
/// кто зацепился за него крюком, и улетает в космос у планеты.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionExtractionComponent : Component
{
    [ViewVariables] public ExtractionStage Stage;
    [ViewVariables] public EntityUid? Beacon;
    [ViewVariables] public EntityUid? Shuttle;
    [ViewVariables] public Vector2 BeaconPosition;

    [DataField] public TimeSpan BeaconBefore = TimeSpan.FromMinutes(5);
    [DataField] public TimeSpan ArriveBefore = TimeSpan.FromMinutes(2);
    [DataField] public TimeSpan DepartBefore = TimeSpan.FromMinutes(1);

    /// <summary>Через сколько после прилёта к планете корабль удаляется.</summary>
    [DataField] public TimeSpan DeleteAfter = TimeSpan.FromMinutes(10);

    /// <summary>Расстояние посадки корабля от маяка, чтобы не раздавить стоящих у маяка.</summary>
    [DataField] public float LandingOffset = 12f;

    /// <summary>Расстояние до планеты, на котором корабль выходит из БСС.</summary>
    [DataField] public float PlanetDistance = 300f;
}

/// <summary>Эвакуационный корабль (alleycat).</summary>
[RegisterComponent]
public sealed partial class ExtractionShuttleComponent : Component
{
    [ViewVariables] public EntityUid Expedition;
    [ViewVariables] public EntityUid Beacon;
    [ViewVariables] public bool Arrived;
    [ViewVariables] public bool Departed;
    [ViewVariables] public TimeSpan DeleteAfter;

    /// <summary>Когда корабль удаляется, оставляя людей в космосе. Ставится по прилёту к планете.</summary>
    [ViewVariables] public TimeSpan? DeleteAt;
}

/// <summary>Игрок, висящий снаружи корабля: не даёт движку вернуть его на карту при полёте.</summary>
[RegisterComponent]
public sealed partial class ExtractionPassengerComponent : Component
{
    [ViewVariables] public EntityUid Grid;
    [ViewVariables] public Vector2 Local;
}
