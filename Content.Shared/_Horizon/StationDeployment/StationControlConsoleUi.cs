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

    public StationControlConsoleBuiState(string stationName)
    {
        StationName = stationName;
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
