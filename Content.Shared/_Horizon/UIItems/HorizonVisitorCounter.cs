// Сгенерировано SS14 UI Editor (предмет «счётчик посетителей»). Не редактируйте вручную.
using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Horizon.UIItems;

[Serializable, NetSerializable]
public enum HorizonVisitorCounterUiKey : byte
{
    Key,
}

/// <summary>Действие из окна: имя действия и значение.</summary>
[Serializable, NetSerializable]
public sealed class HorizonVisitorCounterUiMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public readonly string Value;

    public HorizonVisitorCounterUiMessage(string action, string value)
    {
        Action = action;
        Value = value;
    }
}

/// <summary>Переменные предмета, которые сервер отправляет окну.</summary>
[Serializable, NetSerializable]
public sealed class HorizonVisitorCounterUiState : BoundUserInterfaceState
{
    public readonly Dictionary<string, string> Values;

    public HorizonVisitorCounterUiState(Dictionary<string, string> values)
    {
        Values = values;
    }
}

/// <summary>Переменные предмета «счётчик посетителей» (хранятся на сервере, у каждого экземпляра свои).</summary>
[RegisterComponent]
public sealed partial class HorizonVisitorCounterItemComponent : Component
{
    /// <summary>счёт</summary>
    [DataField("schet")]
    public double V_schet;

    /// <summary>последний</summary>
    [DataField("posledniy")]
    public string V_posledniy = "";
}
