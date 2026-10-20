using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Horizon.GridWelding.Systems;
using Content.Server._Horizon.Shipbreaking;
using Content.Shared.NodeContainer;
using Content.Shared._Horizon.GridWelding.Systems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;

namespace Content.IntegrationTests.Tests._Horizon;

/// <summary>
/// Cutting a part of a grid off (<see cref="ShipCutterSystem"/>) and welding two grids together
/// (<see cref="GridWeldingSystem"/>): what stands on the grids has to survive, and gravity has to follow the generators.
/// </summary>
[TestFixture]
public sealed class GridSplitMergeTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: HorizonGravGenDummy
  components:
  - type: GravityGenerator
  - type: PowerCharge
    windowTitle: gravity-generator-window-title
    idlePower: 50
    chargeRate: 1000000000
    activePower: 500
    activeChargingPower: 500
  - type: ApcPowerReceiver
    needsPower: false
  - type: UserInterface
  - type: Transform
    anchored: true

- type: entity
  id: HorizonLooseDummy
  components:
  - type: Physics
    bodyType: Dynamic
  - type: GravityAffected
";

    private static readonly PoolSettings Settings = new() { FailureLogLevel = LogLevel.Error };

    private static void MakeFloor(IEntityManager entMan, ITileDefinitionManager tileMan, Entity<MapGridComponent> grid, int width, int height)
    {
        var tile = new Tile(tileMan["FloorSteel"].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), tile));
            }
        }

        entMan.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
    }

    private static EntityUid SpawnAnchored(IEntityManager entMan, string proto, EntityUid grid, int x, int y)
    {
        var uid = entMan.SpawnEntity(proto, new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
        var xform = entMan.GetComponent<TransformComponent>(uid);
        if (!xform.Anchored)
            entMan.System<SharedTransformSystem>().AnchorEntity(uid, xform);

        Assert.That(xform.Anchored, Is.True, $"{proto} did not anchor");
        return uid;
    }

    [Test]
    public async Task SplitKeepsAnchoredThingsAndMovesGravity()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.MapMan;

        EntityUid map = default;
        MapId mapId = default;
        Entity<MapGridComponent> grid = default;
        EntityUid generator = default, cable = default, cable2 = default, wall = default, loose = default;

        await server.WaitPost(() =>
        {
            map = server.System<SharedMapSystem>().CreateMap(out mapId);
            grid = mapMan.CreateGridEntity(mapId);
            MakeFloor(entMan, server.ResolveDependency<ITileDefinitionManager>(), grid, 6, 3);

            // The left half (x 0..2) is what gets cut off.
            generator = SpawnAnchored(entMan, "HorizonGravGenDummy", grid, 1, 1);
            cable = SpawnAnchored(entMan, "CableHV", grid, 1, 0);
            cable2 = SpawnAnchored(entMan, "CableHV", grid, 0, 0);
            wall = SpawnAnchored(entMan, "WallSolid", grid, 0, 2);
            loose = entMan.SpawnEntity("HorizonLooseDummy", new EntityCoordinates(grid, 1.5f, 2.5f));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityComponent>(grid).Enabled, Is.True, "the generator should give the grid gravity");
            Assert.That(entMan.GetComponent<GravityAffectedComponent>(loose).Weightless, Is.False);

            var cutter = server.System<ShipCutterSystem>();
            cutter.CutSeam(grid, new Vector2i(2, 0), CutEdges.East);
            cutter.CutSeam(grid, new Vector2i(2, 1), CutEdges.East);
            Assert.That(mapMan.GetAllGrids(mapId).Count(), Is.EqualTo(1), "the seam is still open at the last row, nothing is cut off yet");
            cutter.CutSeam(grid, new Vector2i(2, 2), CutEdges.East);

            var grids = mapMan.GetAllGrids(mapId).ToList();
            Assert.That(grids, Has.Count.EqualTo(2), "cutting the whole seam should split the grid in two");

            var newGrid = grids.First(g => g.Owner != grid.Owner).Owner;

            foreach (var (name, uid) in new[] { ("generator", generator), ("cable", cable), ("wall", wall) })
            {
                Assert.That(entMan.EntityExists(uid) && !entMan.Deleted(uid), Is.True, $"{name} must survive the split");
                var xform = entMan.GetComponent<TransformComponent>(uid);
                Assert.That(xform.Anchored, Is.True, $"{name} must stay anchored");
                Assert.That(xform.GridUid, Is.EqualTo(newGrid), $"{name} must be on the new grid");
            }

            // No cable coil was dropped by a cable that got torn off.
            Assert.That(entMan.GetComponent<MetaDataComponent>(cable).EntityPrototype?.ID, Is.EqualTo("CableHV"));

            Assert.That(entMan.GetComponent<GravityComponent>(newGrid).Enabled, Is.True, "the piece with the generator keeps gravity");
            Assert.That(entMan.GetComponent<GravityComponent>(grid).Enabled, Is.False, "the rest lost its generator");
            Assert.That(entMan.HasComponent<GridCutsComponent>(grid), Is.False, "the cuts along the split are gone");
            Assert.That(entMan.GetComponent<TransformComponent>(loose).GridUid, Is.EqualTo(newGrid), "loose things move with the piece");
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            // The cables are still one network, and what stands on the piece is not left weightless.
            var node1 = entMan.GetComponent<NodeContainerComponent>(cable).Nodes.Values.First();
            var node2 = entMan.GetComponent<NodeContainerComponent>(cable2).Nodes.Values.First();
            Assert.That(node1.NodeGroup, Is.Not.Null);
            Assert.That(node1.NodeGroup, Is.SameAs(node2.NodeGroup), "neighbouring cables stay connected");
            Assert.That(entMan.GetComponent<GravityAffectedComponent>(loose).Weightless, Is.False, "gravity follows the generator");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MergeKeepsThingsAndGravityAndLeavesSeams()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.MapMan;

        MapId mapId = default;
        Entity<MapGridComponent> gridA = default, gridB = default;
        EntityUid generator = default, cable = default, wall = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out mapId);
            var tileMan = server.ResolveDependency<ITileDefinitionManager>();

            gridA = mapMan.CreateGridEntity(mapId);
            MakeFloor(entMan, tileMan, gridA, 3, 3);

            gridB = mapMan.CreateGridEntity(mapId);
            MakeFloor(entMan, tileMan, gridB, 3, 3);
            server.System<SharedTransformSystem>().SetWorldPosition(gridB.Owner, new Vector2(3, 0));

            generator = SpawnAnchored(entMan, "HorizonGravGenDummy", gridB, 1, 1);
            cable = SpawnAnchored(entMan, "CableHV", gridB, 1, 0);
            wall = SpawnAnchored(entMan, "WallSolid", gridB, 0, 2);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityComponent>(gridA).Enabled, Is.False, "grid A has no generator");
            Assert.That(entMan.GetComponent<GravityComponent>(gridB).Enabled, Is.True);

            var welding = server.System<GridWeldingSystem>();
            var shared = server.System<SharedGridWeldingSystem>();

            // Join the east edge of A's tile (2,1) to the west edge of B's tile (0,1).
            var ok = shared.TryComputeWeldGeometry(
                gridA, gridA.Comp, entMan.GetComponent<TransformComponent>(gridA), new Vector2i(2, 1), CutEdges.East,
                gridB, gridB.Comp, entMan.GetComponent<TransformComponent>(gridB), new Vector2i(0, 1), CutEdges.West,
                0.5f, Angle.FromDegrees(10),
                out var offset, out var rotation, out _, out var error);
            Assert.That(ok, Is.True, $"the grids should be weldable: {error}");

            welding.MergeGrids(gridA, gridB, offset, rotation, new Vector2i(2, 1), new Vector2i(0, 1));

            Assert.That(mapMan.GetAllGrids(mapId).Count(), Is.EqualTo(1), "grid B is merged into A");
            Assert.That(entMan.Deleted(gridB.Owner), Is.True);

            foreach (var (name, uid) in new[] { ("generator", generator), ("cable", cable), ("wall", wall) })
            {
                Assert.That(entMan.EntityExists(uid) && !entMan.Deleted(uid), Is.True, $"{name} must survive the merge");
                var xform = entMan.GetComponent<TransformComponent>(uid);
                Assert.That(xform.Anchored, Is.True, $"{name} must stay anchored");
                Assert.That(xform.GridUid, Is.EqualTo(gridA.Owner), $"{name} must be on grid A");
            }

            Assert.That(entMan.GetComponent<GravityComponent>(gridA).Enabled, Is.True, "the generator of B gives the merged grid gravity");

            // The two rows that are not welded stay open as seams, the welded row is sealed.
            var cuts = entMan.GetComponent<GridCutsComponent>(gridA).Cuts;
            Assert.That(cuts.GetValueOrDefault(new Vector2i(2, 0)).HasFlag(CutEdges.East), Is.True);
            Assert.That(cuts.GetValueOrDefault(new Vector2i(3, 0)).HasFlag(CutEdges.West), Is.True);
            Assert.That(cuts.GetValueOrDefault(new Vector2i(2, 2)).HasFlag(CutEdges.East), Is.True);
            Assert.That(cuts.GetValueOrDefault(new Vector2i(3, 2)).HasFlag(CutEdges.West), Is.True);
            Assert.That(cuts.ContainsKey(new Vector2i(2, 1)), Is.False, "the welded point is sealed");

            // Welding the seams shut removes the cuts one by one, and the component with the last one.
            var gridCuts = server.System<GridCutsSystem>();
            Assert.That(gridCuts.RemoveCut(gridA, new Vector2i(2, 0), CutEdges.East), Is.True);
            Assert.That(entMan.HasComponent<GridCutsComponent>(gridA), Is.True);
            Assert.That(gridCuts.RemoveCut(gridA, new Vector2i(2, 2), CutEdges.East), Is.True);
            Assert.That(entMan.HasComponent<GridCutsComponent>(gridA), Is.False, "no seams left");
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MergeWithTurnedGridMovesCutsAndThings()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.MapMan;

        MapId mapId = default;
        Entity<MapGridComponent> gridA = default, gridB = default;
        EntityUid cable = default, wall = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out mapId);
            var tileMan = server.ResolveDependency<ITileDefinitionManager>();

            gridA = mapMan.CreateGridEntity(mapId);
            MakeFloor(entMan, tileMan, gridA, 3, 3);

            // Grid B is turned a quarter turn: its local (x, y) is at (6 - y, x) in the world.
            gridB = mapMan.CreateGridEntity(mapId);
            MakeFloor(entMan, tileMan, gridB, 3, 3);
            server.System<SharedTransformSystem>().SetWorldPositionRotation(gridB.Owner, new Vector2(6, 0), Angle.FromDegrees(90));

            cable = SpawnAnchored(entMan, "CableHV", gridB, 1, 0);
            wall = SpawnAnchored(entMan, "WallSolid", gridB, 0, 2);

            // A cut that grid B already had: the east edge of its tile (0,0).
            server.System<GridCutsSystem>().AddCut(gridB.Owner, new Vector2i(0, 0), CutEdges.East);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var welding = server.System<GridWeldingSystem>();
            var shared = server.System<SharedGridWeldingSystem>();

            // The north edge of B's tile (1,2) faces west once B is turned, towards the east edge of A's tile (2,1).
            var ok = shared.TryComputeWeldGeometry(
                gridA, gridA.Comp, entMan.GetComponent<TransformComponent>(gridA), new Vector2i(2, 1), CutEdges.East,
                gridB, gridB.Comp, entMan.GetComponent<TransformComponent>(gridB), new Vector2i(1, 2), CutEdges.North,
                0.5f, Angle.FromDegrees(10),
                out var offset, out var rotation, out _, out var error);
            Assert.That(ok, Is.True, $"the turned grids should be weldable: {error}");

            welding.MergeGrids(gridA, gridB, offset, rotation, new Vector2i(2, 1), new Vector2i(1, 2));

            Assert.That(mapMan.GetAllGrids(mapId).Count(), Is.EqualTo(1));

            foreach (var (name, uid) in new[] { ("cable", cable), ("wall", wall) })
            {
                var xform = entMan.GetComponent<TransformComponent>(uid);
                Assert.That(xform.Anchored && xform.GridUid == gridA.Owner, Is.True, $"{name} must be anchored on grid A");
            }

            // B's tile (1,0) is the cable: (6 - 0, 1) -> cell (5,1) is wrong, it is (6 - y, x) = (6, 1) minus a tile.
            var cableTile = entMan.System<SharedMapSystem>().LocalToTile(gridA.Owner, gridA.Comp, entMan.GetComponent<TransformComponent>(cable).Coordinates);
            Assert.That(cableTile, Is.EqualTo(new Vector2i(5, 1)), "the cable ends up where its tile went");

            // B's own cut moved and turned: east of tile (0,0) is north of cell (5,0) after the quarter turn.
            var cuts = entMan.GetComponent<GridCutsComponent>(gridA).Cuts;
            Assert.That(cuts.GetValueOrDefault(new Vector2i(5, 0)).HasFlag(CutEdges.North), Is.True);
            Assert.That(cuts.GetValueOrDefault(new Vector2i(5, 1)).HasFlag(CutEdges.South), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CutsSurviveSaving()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var seri = server.ResolveDependency<ISerializationManager>();

        await server.WaitAssertion(() =>
        {
            var cuts = new Dictionary<Vector2i, CutEdges>
            {
                [new Vector2i(2, 1)] = CutEdges.East | CutEdges.North,
                [new Vector2i(-3, 4)] = CutEdges.West,
            };

            var node = seri.WriteValue(cuts, notNullableOverride: true);
            var read = seri.Read<Dictionary<Vector2i, CutEdges>>(node, notNullableOverride: true);

            Assert.That(read, Is.EquivalentTo(cuts));
        });

        await pair.CleanReturnAsync();
    }
}
