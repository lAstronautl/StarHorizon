using System.Numerics;
using Content.Server.EUI;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.ShipBuilder;

/// <summary>
/// Верфь: создаёт для игрока отдельную карту с пустым гридом и камерой,
/// на которой он может ставить тайлы и стены.
/// </summary>
public sealed class ShipBuilderSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _eui = default!;
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
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<ShipBuilderConsoleComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        args.Handled = true;
        Open(actor.PlayerSession);
    }

    public void Open(ICommonSession session)
    {
        _eui.OpenEui(new ShipBuilderEui(this), session);
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
