using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.PaymentTerminal;

[Serializable, NetSerializable]
public enum PaymentTerminalUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum PaymentTerminalMode : byte
{
    /// <summary>
    /// The owner sets the price, the buyer only adds a tip.
    /// </summary>
    Fixed,

    /// <summary>
    /// The buyer enters both the price and the tip.
    /// </summary>
    Free,
}

[Serializable, NetSerializable]
public enum PaymentTerminalField : byte
{
    Price,
    Tip,
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalBoundUserInterfaceState(
    string? ownerName,
    PaymentTerminalMode mode,
    PaymentTerminalField selectedField,
    int price,
    int tip,
    float taxRate) : BoundUserInterfaceState
{
    public readonly string? OwnerName = ownerName;
    public readonly PaymentTerminalMode Mode = mode;
    public readonly PaymentTerminalField SelectedField = selectedField;
    public readonly int Price = price;
    public readonly int Tip = tip;
    public readonly float TaxRate = taxRate;
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalKeypadMessage(int digit) : BoundUserInterfaceMessage
{
    public readonly int Digit = digit;
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalClearMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PaymentTerminalSelectFieldMessage(PaymentTerminalField field) : BoundUserInterfaceMessage
{
    public readonly PaymentTerminalField Field = field;
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalToggleModeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PaymentTerminalUnbindMessage : BoundUserInterfaceMessage;
