namespace Content.Server._StarHorizon.Expedition;

public enum ExtractionStage : byte
{
    None,
    ShuttleSent,
    Departed,
}

/// <summary>
/// Эвакуация с планетной экспедиции. Висит на карте экспедиции: за <see cref="ArriveBefore"/> до конца прилетает
/// alleycat, за <see cref="DepartBefore"/> он улетает в космос у планеты с теми, кто стоит на борту.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionExtractionComponent : Component
{
    [ViewVariables] public ExtractionStage Stage;
    [ViewVariables] public EntityUid? Shuttle;

    /// <summary>За сколько до конца экспедиции шаттл летит на карту.</summary>
    [DataField] public TimeSpan ArriveBefore = TimeSpan.FromMinutes(2);

    /// <summary>За сколько до конца экспедиции шаттл улетает.</summary>
    [DataField] public TimeSpan DepartBefore = TimeSpan.FromSeconds(30);

    /// <summary>Через сколько после прилёта к планете шаттл удаляется.</summary>
    [DataField] public TimeSpan DeleteAfter = TimeSpan.FromMinutes(10);

    /// <summary>Смещение посадки от места приземления капсул, чтобы не раздавить людей.</summary>
    [DataField] public float LandingOffset = 12f;

    /// <summary>Расстояние до планеты, на котором шаттл выходит из БСС.</summary>
    [DataField] public float PlanetDistance = 300f;
}

/// <summary>Эвакуационный шаттл (alleycat).</summary>
[RegisterComponent]
public sealed partial class ExtractionShuttleComponent : Component
{
    [ViewVariables] public EntityUid Expedition;
    [ViewVariables] public bool Arrived;
    [ViewVariables] public bool Departed;
    [ViewVariables] public TimeSpan DeleteAfter;

    /// <summary>Когда шаттл удаляется, оставляя людей в космосе. Ставится по прилёту к планете.</summary>
    [ViewVariables] public TimeSpan? DeleteAt;
}
