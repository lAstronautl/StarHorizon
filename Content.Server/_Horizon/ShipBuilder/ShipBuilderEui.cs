using Content.Server.EUI;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Eui;
using Robust.Server.GameStates;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.ShipBuilder;

public sealed class ShipBuilderEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly ShipBuilderSystem _builder;
    private readonly SharedViewSubscriberSystem _viewSubscriber;

    private EntityUid _map;
    private EntityUid _grid;
    private EntityUid _camera;

    public ShipBuilderEui(ShipBuilderSystem builder)
    {
        IoCManager.InjectDependencies(this);
        _builder = builder;
        _viewSubscriber = _entManager.System<SharedViewSubscriberSystem>();
    }

    public override void Opened()
    {
        base.Opened();

        (_map, _grid, _camera) = _builder.CreateWorkspace(Player.Name);

        // Игрок получает всё, что находится рядом с камерой.
        _viewSubscriber.AddViewSubscriber(_camera, Player);
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();
        _builder.DeleteWorkspace(_map);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case ShipBuilderMoveCameraMessage move:
                if (float.IsFinite(move.Delta.X) && float.IsFinite(move.Delta.Y))
                    _builder.MoveCamera(_camera, move.Delta);
                break;
            case ShipBuilderPlaceMessage place:
                if (_builder.Interact(_grid, place.World, place.Tool, place.Id))
                    StateDirty();
                break;
        }
    }

    public override EuiStateBase GetNewState()
    {
        var (tiles, walls) = _builder.Count(_grid);
        return new ShipBuilderEuiState(_entManager.GetNetEntity(_camera), _timing.CurTick, tiles, walls);
    }
}
