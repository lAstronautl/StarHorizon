// Сгенерировано SS14 UI Editor (предмет «доска объявлений»). Не редактируйте вручную.
using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Horizon.UIItems;

[Serializable, NetSerializable]
public enum HorizonNoticeBoardUiKey : byte
{
    Key,
}

/// <summary>Действие из окна: имя действия и значение.</summary>
[Serializable, NetSerializable]
public sealed class HorizonNoticeBoardUiMessage : BoundUserInterfaceMessage
{
    public readonly string Action;
    public readonly string Value;

    public HorizonNoticeBoardUiMessage(string action, string value)
    {
        Action = action;
        Value = value;
    }
}

/// <summary>Переменные предмета, которые сервер отправляет окну.</summary>
[Serializable, NetSerializable]
public sealed class HorizonNoticeBoardUiState : BoundUserInterfaceState
{
    public readonly Dictionary<string, string> Values;

    public HorizonNoticeBoardUiState(Dictionary<string, string> values)
    {
        Values = values;
    }
}

/// <summary>Переменные предмета «доска объявлений» (хранятся на сервере, у каждого экземпляра свои).</summary>
[RegisterComponent]
public sealed partial class HorizonNoticeBoardItemComponent : Component
{
    /// <summary>текст</summary>
    [DataField("tekst")]
    public string V_tekst = "";

    /// <summary>автор</summary>
    [DataField("avtor")]
    public string V_avtor = "";

    /// <summary>всего</summary>
    [DataField("vsego")]
    public double V_vsego;
}
