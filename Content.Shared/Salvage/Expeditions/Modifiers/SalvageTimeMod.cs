using Robust.Shared.Prototypes;

namespace Content.Shared.Salvage.Expeditions.Modifiers;

/// <summary>
/// Редкий модификатор времени экспедиции (StarHorizon): сдвигает длительность на <see cref="Offset"/> секунд.
/// Выпадает по шансу <see cref="Prob"/> (по сиду задания, поэтому клиент и сервер видят одно и то же).
/// </summary>
[Prototype("salvageTimeMod")]
public sealed partial class SalvageTimeMod : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("desc")] public LocId Description { get; private set; } = string.Empty;

    /// <summary>Сдвиг длительности, секунды (минус сокращает, плюс увеличивает).</summary>
    [DataField] public int Offset { get; private set; }

    /// <summary>Шанс выпадения у задания, 0..1.</summary>
    [DataField] public float Prob { get; private set; } = 0.07f;
}
