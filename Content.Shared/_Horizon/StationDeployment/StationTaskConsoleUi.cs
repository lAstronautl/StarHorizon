using Content.Shared._Horizon.StationDeployment.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.StationDeployment;

[Serializable, NetSerializable]
public enum StationTaskConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public readonly record struct StationOrderUiEntry(string Id, ProtoId<StationOrderPrototype> Order);

/// <summary>
/// A category's development level plus the raw progress within the current level, so the console
/// can show e.g. "Level 0 (1/3)" instead of only the level number - which stays at 0 (and looks
/// like no progress at all) until a full level's worth of orders is completed.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct StationCategoryProgress(int Level, int Progress, int OrdersPerLevel);

[Serializable, NetSerializable]
public sealed class StationTaskConsoleBuiState : BoundUserInterfaceState
{
    public readonly List<StationOrderUiEntry> Orders;
    public readonly Dictionary<ProtoId<TechDisciplinePrototype>, StationCategoryProgress> Levels;
    public readonly bool CapsulePresent;
    public readonly bool CapsuleDocked;

    public StationTaskConsoleBuiState(
        List<StationOrderUiEntry> orders,
        Dictionary<ProtoId<TechDisciplinePrototype>, StationCategoryProgress> levels,
        bool capsulePresent,
        bool capsuleDocked)
    {
        Orders = orders;
        Levels = levels;
        CapsulePresent = capsulePresent;
        CapsuleDocked = capsuleDocked;
    }
}

[Serializable, NetSerializable]
public sealed class StationOrderSummonCapsuleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class StationOrderRecallCapsuleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class StationOrderCancelMessage : BoundUserInterfaceMessage
{
    public readonly string OrderId;

    public StationOrderCancelMessage(string orderId)
    {
        OrderId = orderId;
    }
}
