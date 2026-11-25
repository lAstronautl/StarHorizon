using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.Expedition;

/// <summary>
/// Ключ шифрования с музыкой экспедиции: пока он вставлен в гарнитуру игрока, у того локально играет музыка. Музыка
/// каждой сцены задаётся в прототипе ключа: либо одним файлом (<c>!type:SoundPathSpecifier</c>), либо набором
/// (<c>!type:SoundCollectionSpecifier</c> с коллекцией звуков), так что свои ключи делаются через прототипы.
/// Сцена без музыки в ключе молчит.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ExpeditionMusicKeyComponent : Component
{
    /// <summary>Игрок летит в БСС в дроп-поде (один трек по кругу).</summary>
    [DataField] public SoundSpecifier? Rig;

    /// <summary>Стингер, когда экран переключился на место посадки капсулы (потом идёт <see cref="Ambiance"/>).</summary>
    [DataField] public SoundSpecifier? Tittle;

    /// <summary>Фон на экспедиции, пока нет волны (перемешанно по кругу).</summary>
    [DataField] public SoundSpecifier? Ambiance;

    /// <summary>Музыка волны врагов (перемешанно по кругу до конца волны).</summary>
    [DataField] public SoundSpecifier? Wave;

    /// <summary>Музыка эвакуации (перемешанно по кругу).</summary>
    [DataField] public SoundSpecifier? Extraction;

    /// <summary>Победа: игрок на эвакуационном шаттле, который ушёл в БСС (один раз).</summary>
    [DataField] public SoundSpecifier? Win;

    /// <summary>Игрок погиб (не чаще раза в 5 минут).</summary>
    [DataField] public SoundSpecifier? Loss;
}

/// <summary>На карте экспедиции: идёт волна врагов (клиент играет боевую музыку до <see cref="End"/>).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ExpeditionWaveStateComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    /// <summary>Когда волна считается закончившейся (по серверному времени).</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan End;
}

/// <summary>На карте экспедиции: идёт эвакуация, шаттл отправлен и ещё не улетел (клиент играет музыку эвакуации).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExpeditionExtractionStateComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;
}

/// <summary>На гриде эвакуационного шаттла: он ушёл в БСС (у находящихся на борту играет победная музыка).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExtractionShuttleMarkerComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Departed;
}
