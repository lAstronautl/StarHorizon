using System.Numerics;
using Robust.Server.GameStates;
using Robust.Shared.Player;
using Content.Shared._Horizon.ShipBuilder;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.ShipBuilder;

/// <summary>
/// Верфь: создаёт для игрока отдельную карту с пустым гридом и камерой,
/// на которой он может ставить тайлы и стены.
/// </summary>
public sealed class ShipBuilderSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MapSystem _maps = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;

    private static readonly EntProtoId CameraProto = "ShipBuilderCamera";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipBuilderConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ShipBuilderPlaceMessage>(OnPlace);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ShipBuilderMoveCameraMessage>(OnMoveCamera);
    }

    private void OnOpened(Entity<ShipBuilderConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(ShipBuilderUiKey.Key) || !TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        // Один пользователь на консоль: старая рабочая область заменяется новой.
        DeleteWorkspace(ent.Comp);

        var (map, grid, camera) = CreateWorkspace(actor.PlayerSession.Name);
        ent.Comp.Map = map;
        ent.Comp.Grid = grid;
        ent.Comp.Camera = camera;
        ent.Comp.User = args.Actor;

        // Игрок получает всё, что находится рядом с камерой.
        _viewSubscriber.AddViewSubscriber(camera, actor.PlayerSession);
        UpdateUi(ent);
    }

    private void OnClosed(Entity<ShipBuilderConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(ShipBuilderUiKey.Key) || ent.Comp.User != args.Actor)
            return;

        DeleteWorkspace(ent.Comp);
    }

    private void OnShutdown(Entity<ShipBuilderConsoleComponent> ent, ref ComponentShutdown args)
    {
        DeleteWorkspace(ent.Comp);
    }

    private void OnPlace(Entity<ShipBuilderConsoleComponent> ent, ref ShipBuilderPlaceMessage args)
    {
        if (ent.Comp.User != args.Actor)
            return;

        if (Interact(ent.Comp.Grid, args.World, args.Tool, args.Id))
            UpdateUi(ent);
    }

    private void OnMoveCamera(Entity<ShipBuilderConsoleComponent> ent, ref ShipBuilderMoveCameraMessage args)
    {
        if (ent.Comp.User != args.Actor)
            return;

        if (float.IsFinite(args.Delta.X) && float.IsFinite(args.Delta.Y))
            MoveCamera(ent.Comp.Camera, args.Delta);
    }

    private void UpdateUi(Entity<ShipBuilderConsoleComponent> ent)
    {
        var (tiles, walls) = Count(ent.Comp.Grid);
        _ui.SetUiState(ent.Owner, ShipBuilderUiKey.Key, new ShipBuilderBuiState(GetNetEntity(ent.Comp.Camera), tiles, walls));
    }

    private void DeleteWorkspace(ShipBuilderConsoleComponent comp)
    {
        if (comp.Map.Valid)
            DeleteWorkspace(comp.Map);

        comp.Map = EntityUid.Invalid;
        comp.Grid = EntityUid.Invalid;
        comp.Camera = EntityUid.Invalid;
        comp.User = null;
    }

    /// <summary>Создаёт карту строительства, грид и камеру.</summary>
    public (EntityUid Map, EntityUid Grid, EntityUid Camera) CreateWorkspace(string ownerName)
    {
        var mapUid = _maps.CreateMap(out var mapId);
        _meta.SetEntityName(mapUid, $"SHIPBUILDER-{ownerName}");

        // Чтобы на карте было светло.
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = Color.White;
        Dirty(mapUid, light);

        var grid = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(grid.Owner, $"SHIPBUILDER-GRID-{ownerName}");

        // Стартовый тайл, чтобы было с чего начинать.
        _maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(_tileDefs[ShipBuilderPalette.Tiles[0]].TileId));

        var camera = Spawn(CameraProto, new EntityCoordinates(mapUid, Vector2.Zero));

        // Вид-чертёж без динамического освещения: иначе пол и стены почти не видны.
        _eye.SetDrawLight((camera, null), false);
        return (mapUid, grid.Owner, camera);
    }

    public void MoveCamera(EntityUid camera, Vector2 delta)
    {
        if (!Exists(camera))
            return;

        var pos = Transform(camera).LocalPosition + delta;
        var max = ShipBuilderPalette.MaxExtent;
        pos = Vector2.Clamp(pos, new Vector2(-max), new Vector2(max));
        _transform.SetLocalPosition(camera, pos);
    }

    public (int Tiles, int Walls) Count(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var comp))
            return (0, 0);

        var tiles = 0;
        var walls = 0;
        var enumerator = _maps.GetAllTilesEnumerator(grid, comp);
        while (enumerator.MoveNext(out var tile))
        {
            tiles++;
            walls += CountWalls(grid, comp, tile.Value.GridIndices);
        }

        return (tiles, walls);
    }

    private int CountWalls(EntityUid grid, MapGridComponent comp, Vector2i cell)
    {
        var count = 0;
        foreach (var _ in _maps.GetAnchoredEntities(grid, comp, cell))
            count++;
        return count;
    }

    /// <summary>Обрабатывает клик игрока. Возвращает true, если что-то изменилось.</summary>
    public bool Interact(EntityUid grid, Vector2 world, ShipBuilderTool tool, string id)
    {
        if (!TryComp<MapGridComponent>(grid, out var comp))
            return false;

        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y))
            return false;

        var cell = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (Math.Abs(cell.X) > ShipBuilderPalette.MaxExtent || Math.Abs(cell.Y) > ShipBuilderPalette.MaxExtent)
            return false;

        switch (tool)
        {
            case ShipBuilderTool.Tile:
                return PlaceTile(grid, comp, cell, id);
            case ShipBuilderTool.Wall:
                return PlaceWall(grid, comp, cell, id);
            case ShipBuilderTool.Erase:
                return Erase(grid, comp, cell);
            default:
                return false;
        }
    }

    private bool PlaceTile(EntityUid grid, MapGridComponent comp, Vector2i cell, string id)
    {
        if (Array.IndexOf(ShipBuilderPalette.Tiles, id) < 0)
            return false;

        var def = _tileDefs[id];
        var current = _maps.GetTileRef(grid, comp, cell).Tile;
        if (current.TypeId == def.TileId)
            return false;

        if (current.IsEmpty && Count(grid).Tiles >= ShipBuilderPalette.MaxTiles)
            return false;

        _maps.SetTile(grid, comp, cell, new Tile(def.TileId));
        return true;
    }

    private bool PlaceWall(EntityUid grid, MapGridComponent comp, Vector2i cell, string id)
    {
        if (Array.IndexOf(ShipBuilderPalette.Walls, id) < 0 || !_proto.HasIndex<EntityPrototype>(id))
            return false;

        // Стены ставятся только на тайл и только по одной в клетке.
        if (_maps.GetTileRef(grid, comp, cell).Tile.IsEmpty)
            return false;

        foreach (var _ in _maps.GetAnchoredEntities(grid, comp, cell))
            return false;

        var wall = Spawn(id, _maps.GridTileToLocal(grid, comp, cell));
        var xform = Transform(wall);
        if (!xform.Anchored)
            _transform.AnchorEntity(wall, xform);

        return true;
    }

    private bool Erase(EntityUid grid, MapGridComponent comp, Vector2i cell)
    {
        // Сначала стираем стену, затем сам тайл.
        var anchored = new List<EntityUid>();
        foreach (var ent in _maps.GetAnchoredEntities(grid, comp, cell))
            anchored.Add(ent);

        if (anchored.Count > 0)
        {
            foreach (var ent in anchored)
                QueueDel(ent);
            return true;
        }

        if (_maps.GetTileRef(grid, comp, cell).Tile.IsEmpty)
            return false;

        _maps.SetTile(grid, comp, cell, Tile.Empty);
        return true;
    }

    public void DeleteWorkspace(EntityUid map)
    {
        if (Exists(map))
            Del(map);
    }
}
