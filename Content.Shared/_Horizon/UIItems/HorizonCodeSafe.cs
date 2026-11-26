// Сгенерировано SS14 UI Editor (предмет «кодовый сейф»). Не редактируйте вручную.
using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Horizon.UIItems;

[Serializable, NetSerializable]
public enum HorizonCodeSafeUiKey : byte
{
    Key,
}

/// <summary>Действие из окна: имя действия и значение.</summary>
[Serializable, NetSerializable]
public sealed class HorizonCodeSafeUiMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public readonly string Value;

    public HorizonCodeSafeUiMessage(string action, string value)
    {
        Action = action;
        Value = value;
    }
}

/// <summary>Переменные предмета, которые сервер отправляет окну.</summary>
[Serializable, NetSerializable]
public sealed class HorizonCodeSafeUiState : BoundUserInterfaceState
{
    public readonly Dictionary<string, string> Values;

    public HorizonCodeSafeUiState(Dictionary<string, string> values)
    {
        Values = values;
    }
}

/// <summary>Переменные предмета «кодовый сейф» (хранятся на сервере, у каждого экземпляра свои).</summary>
[RegisterComponent]
public sealed partial class HorizonCodeSafeItemComponent : Component
{
    /// <summary>код</summary>
    [DataField("kod")]
    public string V_kod = "";

    /// <summary>ошибок</summary>
    [DataField("oshibok")]
    public double V_oshibok;

    /// <summary>открыт</summary>
    [DataField("otkryt")]
    public bool V_otkryt;
}
