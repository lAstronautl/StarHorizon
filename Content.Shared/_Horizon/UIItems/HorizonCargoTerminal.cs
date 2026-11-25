// Сгенерировано SS14 UI Editor (предмет «грузовой терминал»). Не редактируйте вручную.
using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Horizon.UIItems;

[Serializable, NetSerializable]
public enum HorizonCargoTerminalUiKey : byte
{
    Key,
}

/// <summary>Действие из окна: имя действия и значение.</summary>
[Serializable, NetSerializable]
public sealed class HorizonCargoTerminalUiMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public readonly string Value;

    public HorizonCargoTerminalUiMessage(string action, string value)
    {
        Action = action;
        Value = value;
    }
}

/// <summary>Переменные предмета, которые сервер отправляет окну.</summary>
[Serializable, NetSerializable]
public sealed class HorizonCargoTerminalUiState : BoundUserInterfaceState
{
    public readonly Dictionary<string, string> Values;

    public HorizonCargoTerminalUiState(Dictionary<string, string> values)
    {
        Values = values;
    }
}

/// <summary>Переменные предмета «грузовой терминал» (хранятся на сервере, у каждого экземпляра свои).</summary>
[RegisterComponent]
public sealed partial class HorizonCargoTerminalItemComponent : Component
{
    /// <summary>баланс</summary>
    [DataField("balans")]
    public double V_balans;

    /// <summary>заказов</summary>
    [DataField("zakazov")]
    public double V_zakazov;
}
