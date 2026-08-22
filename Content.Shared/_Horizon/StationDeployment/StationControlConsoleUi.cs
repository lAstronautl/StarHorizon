using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.StationDeployment;

[Serializable, NetSerializable]
public enum StationControlConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class StationControlConsoleBuiState : BoundUserInterfaceState
{
    public readonly string StationName;

    /// <summary>
    /// The station's own bank account balance (credited by capsule sales) and whether the ATM
    /// controls should currently be enabled, plus the value of any cash inserted into the slot.
    /// </summary>
    public readonly int Balance;
    public readonly bool BankEnabled;
    public readonly int Deposit;

    public StationControlConsoleBuiState(string stationName, int balance, bool bankEnabled, int deposit)
    {
        StationName = stationName;
        Balance = balance;
        BankEnabled = bankEnabled;
        Deposit = deposit;
    }
}

[Serializable, NetSerializable]
public sealed class StationControlConsoleRenameMessage : BoundUserInterfaceMessage
{
    public readonly string Name;

    public StationControlConsoleRenameMessage(string name)
    {
        Name = name;
    }
}
