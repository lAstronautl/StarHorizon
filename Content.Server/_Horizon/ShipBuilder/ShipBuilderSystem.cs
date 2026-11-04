using System.Numerics;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Server.GameStates;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.ShipBuilder;

/// <summary>
/// Верфь: создаёт для игрока отдельную карту с пустым гридом и камерой, на которой он может
/// ставить тайлы и предметы. Список доступных предметов задаётся прототипом консоли и
/// расширяется чертежами, применёнными к серверу верфи.
/// </summary>
public sealed class ShipBuilderSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MapSystem _maps = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly EntProtoId CameraProto = "ShipBuilderCamera";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipBuilderConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ShipBuilderPlaceMessage>(OnPlace);
        SubscribeLocalEvent<ShipBuilderConsoleComponent, ShipBuilderMoveCameraMessage>(OnMoveCamera);

        SubscribeLocalEvent<ShipBuilderBlueprintComponent, AfterInteractEvent>(OnBlueprintAfterInteract);
    }

    #region Чертежи

    private void OnBlueprintAfterInteract(EntityUid blueprintUid, ShipBuilderBlueprintComponent blueprint, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        // Чертёж можно применить к серверу верфи напрямую или через консоль.
        Entity<ShipBuilderServerComponent>? server = null;
        if (TryComp<ShipBuilderServerComponent>(target, out var serverComp))
            server = (target, serverComp);
        else if (HasComp<ShipBuilderConsoleComponent>(target))
            server = FindServer(target);
        else
            return;

        args.Handled = true;

        if (server == null)
        {
            _popup.PopupEntity(Loc.GetString("ship-builder-blueprint-no-server"), target, args.User);
            return;
        }

        var added = 0;
        foreach (var id in blueprint.Unlocks)
        {
            if (!_proto.HasIndex<EntityPrototype>(id))
            {
                Log.Warning($"Чертёж {ToPrettyString(blueprintUid)} ссылается на несуществующий прототип {id}");
                continue;
            }

            if (server.Value.Comp.Unlocked.Add(id))
                added++;
        }

        if (added == 0)
        {
            _popup.PopupEntity(Loc.GetString("ship-builder-blueprint-known"), target, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("ship-builder-blueprint-unlocked", ("count", added)), target, args.User);
        QueueDel(blueprintUid);

        // Открытые окна консолей, использующих этот сервер, получают новый список.
        var query = EntityQueryEnumerator<ShipBuilderConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (console.User != null)
                UpdateUi((uid, console));
        }
    }

    /// <summary>Ищет сервер верфи на том же гриде (или на той же карте, если консоль не на гриде).</summary>
    public Entity<ShipBuilderServerComponent>? FindServer(EntityUid console)
    {
        var xform = Transform(console);
        var query = EntityQueryEnumerator<ShipBuilderServerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var server, out var serverXform))
        {
            var sameGrid = xform.GridUid != null
                ? serverXform.GridUid == xform.GridUid
                : serverXform.MapUid == xform.MapUid;

            if (sameGrid)
                return (uid, server);
        }

        return null;
    }

    /// <summary>Предметы, доступные консоли: базовые из прототипа плюс разблокированные на сервере.</summary>
    private List<string> GetAvailableEntities(Entity<ShipBuilderConsoleComponent> console)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        foreach (var id in console.Comp.Entities)
        {
            if (_proto.HasIndex<EntityPrototype>(id) && seen.Add(id))
                result.Add(id);
        }

        if (FindServer(console) is { } server)
        {
            foreach (var id in server.Comp.Unlocked)
            {
                if (_proto.HasIndex<EntityPrototype>(id) && seen.Add(id))
                    result.Add(id);
            }
        }

        return result;
    }

    private List<string> GetAvailableTiles(ShipBuilderConsoleComponent console)
    {
        var result = new List<string>();
        foreach (var id in console.Tiles)
        {
            if (_proto.HasIndex<ContentTileDefinition>(id))
                result.Add(id);
        }

        return result;
    }

    #endregion

    #region Интерфейс

    private void OnOpened(Entity<ShipBuilderConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(ShipBuilderUiKey.Key) || !TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        // Один пользователь на консоль: старая рабочая область заменяется новой.
        DeleteWorkspace(ent.Comp);

        var tiles = GetAvailableTiles(ent.Comp);
        var (map, grid, camera) = CreateWorkspace(actor.PlayerSession.Name, tiles.Count > 0 ? tiles[0] : null);
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
        if (ent.Comp.User != args.Actor || !ent.Comp.Grid.Valid)
            return;

        if (Interact(ent, args.World, args.Tool, args.Id, args.Rotation))
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
        var (tiles, objects) = Count(ent.Comp.Grid);
        NetEntity? camera = ent.Comp.Camera.Valid ? GetNetEntity(ent.Comp.Camera) : null;

        var state = new ShipBuilderBuiState(
            camera,
            tiles,
            objects,
            GetAvailableTiles(ent.Comp),
            GetAvailableEntities(ent),
            FindServer(ent) != null);

        _ui.SetUiState(ent.Owner, ShipBuilderUiKey.Key, state);
    }

    #endregion

    #region Рабочая область

    private void DeleteWorkspace(ShipBuilderConsoleComponent comp)
    {
        if (comp.Map.Valid)
            DeleteWorkspace(comp.Map);

        comp.Map = EntityUid.Invalid;
        comp.Grid = EntityUid.Invalid;
        comp.Camera = EntityUid.Invalid;
        comp.User = null;
    }

    public void DeleteWorkspace(EntityUid map)
    {
        if (Exists(map))
            Del(map);
    }

    /// <summary>Создаёт карту строительства, грид и камеру.</summary>
    private (EntityUid Map, EntityUid Grid, EntityUid Camera) CreateWorkspace(string ownerName, string? startTile)
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
        if (startTile != null)
            _maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(_tileDefs[startTile].TileId));

        var camera = Spawn(CameraProto, new EntityCoordinates(mapUid, Vector2.Zero));

        // Вид-чертёж без динамического освещения: иначе пол и стены почти не видны.
        _eye.SetDrawLight((camera, null), false);
        return (mapUid, grid.Owner, camera);
    }

    private void MoveCamera(EntityUid camera, Vector2 delta)
    {
        if (!Exists(camera))
            return;

        var pos = Transform(camera).LocalPosition + delta;
        var max = ShipBuilderPalette.MaxCameraExtent;
        pos = Vector2.Clamp(pos, new Vector2(-max), new Vector2(max));
        _transform.SetLocalPosition(camera, pos);
    }

    private (int Tiles, int Objects) Count(EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var comp))
            return (0, 0);

        var tiles = 0;
        var objects = 0;
        var enumerator = _maps.GetAllTilesEnumerator(grid, comp);
        while (enumerator.MoveNext(out var tile))
        {
            tiles++;
            foreach (var _ in _maps.GetAnchoredEntities(grid, comp, tile.Value.GridIndices))
                objects++;
        }

        return (tiles, objects);
    }

    #endregion

    #region Строительство

    /// <summary>Обрабатывает клик игрока. Возвращает true, если что-то изменилось.</summary>
    private bool Interact(Entity<ShipBuilderConsoleComponent> console, Vector2 world, ShipBuilderTool tool, string id, int rotation)
    {
        var grid = console.Comp.Grid;
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
                return GetAvailableTiles(console.Comp).Contains(id) && PlaceTile(grid, comp, cell, id);
            case ShipBuilderTool.Entity:
                return GetAvailableEntities(console).Contains(id) && PlaceEntity(grid, comp, cell, id, rotation);
            case ShipBuilderTool.Erase:
                return Erase(grid, comp, cell);
            default:
                return false;
        }
    }

    private bool PlaceTile(EntityUid grid, MapGridComponent comp, Vector2i cell, string id)
    {
        var def = _tileDefs[id];
        var current = _maps.GetTileRef(grid, comp, cell).Tile;
        if (current.TypeId == def.TileId)
            return false;

        if (current.IsEmpty && Count(grid).Tiles >= ShipBuilderPalette.MaxTiles)
            return false;

        _maps.SetTile(grid, comp, cell, new Tile(def.TileId));
        return true;
    }

    private bool PlaceEntity(EntityUid grid, MapGridComponent comp, Vector2i cell, string id, int rotation)
    {
        // Ставить можно только на тайл.
        if (_maps.GetTileRef(grid, comp, cell).Tile.IsEmpty)
            return false;

        if (Count(grid).Objects >= ShipBuilderPalette.MaxObjects)
            return false;

        var angle = Angle.FromDegrees(((rotation % 360) + 360) % 360 / 90 * 90);

        // Не ставим одинаковый предмет с тем же поворотом в одну клетку.
        foreach (var other in _maps.GetAnchoredEntities(grid, comp, cell))
        {
            if (MetaData(other).EntityPrototype?.ID == id && Transform(other).LocalRotation.EqualsApprox(angle))
                return false;
        }

        var ent = Spawn(id, _maps.GridTileToLocal(grid, comp, cell));
        _transform.SetLocalRotation(ent, angle);

        var xform = Transform(ent);
        if (!xform.Anchored && IsStatic(ent))
            _transform.AnchorEntity(ent, xform);

        // Два твёрдых препятствия в одной клетке не допускаем (стена в стене).
        if (IsSolid(ent))
        {
            foreach (var other in _maps.GetAnchoredEntities(grid, comp, cell))
            {
                if (other != ent && IsSolid(other))
                {
                    Del(ent);
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsStatic(EntityUid uid)
    {
        return TryComp<PhysicsComponent>(uid, out var body) && body.BodyType == BodyType.Static;
    }

    private bool IsSolid(EntityUid uid)
    {
        return TryComp<PhysicsComponent>(uid, out var body)
               && body.BodyType == BodyType.Static
               && body.Hard
               && (body.CollisionLayer & (int) CollisionGroup.Impassable) != 0;
    }

    private bool Erase(EntityUid grid, MapGridComponent comp, Vector2i cell)
    {
        // Сначала стираем объекты в клетке, затем сам тайл.
        var toDelete = new HashSet<EntityUid>();
        foreach (var ent in _maps.GetAnchoredEntities(grid, comp, cell))
            toDelete.Add(ent);

        var center = _maps.GridTileToLocal(grid, comp, cell);
        var loose = new HashSet<Entity<TransformComponent>>();
        _lookup.GetEntitiesInRange(_transform.ToMapCoordinates(center), 0.45f, loose);
        foreach (var (uid, xform) in loose)
        {
            if (xform.ParentUid == grid && uid != grid)
                toDelete.Add(uid);
        }

        if (toDelete.Count > 0)
        {
            foreach (var ent in toDelete)
                QueueDel(ent);

            return true;
        }

        if (_maps.GetTileRef(grid, comp, cell).Tile.IsEmpty)
            return false;

        _maps.SetTile(grid, comp, cell, Tile.Empty);
        return true;
    }

    #endregion
}
