namespace Content.Server._StarHorizon.Expedition;

public enum ExtractionStage : byte
{
    None,
    ShuttleSent,
    Departed,
}

/// <summary>
/// Эвакуация с планетной экспедиции. Висит на карте экспедиции: за <see cref="ArriveBefore"/> до конца прилетает
/// alleycat, за ~5 секунд до конца (как экспедиционный шаттл: старт FTL с запасом <see cref="DepartMargin"/>) он улетает в космос у планеты с теми, кто стоит на борту.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionExtractionComponent : Component
{
    [ViewVariables] public ExtractionStage Stage;
    [ViewVariables] public EntityUid? Shuttle;

    /// <summary>За сколько до конца экспедиции шаттл летит на карту.</summary>
    [DataField] public TimeSpan ArriveBefore = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Шаттл улетает как экспедиционный: когда до конца остаётся время старта FTL плюс этот запас, прыжок начинается
    /// так, чтобы шаттл ушёл в БСС ровно в конец экспедиции.
    /// </summary>
    [DataField] public TimeSpan DepartMargin = TimeSpan.FromSeconds(0.5);

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
