using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.Expedition;

/// <summary>
/// Маркер на ключе шифрования: пока ключ вставлен в гарнитуру игрока, у него локально играет музыка экспедиции.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ExpeditionMusicKeyComponent : Component;

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
