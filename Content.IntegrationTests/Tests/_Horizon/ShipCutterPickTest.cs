using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Horizon.Shipbreaking;
using Content.Shared._Horizon.Shipbreaking;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Horizon;

/// <summary>
/// What a click with the cutter is aimed at: the seam near a tile edge (also under a wall), else the wall.
/// </summary>
[TestFixture]
public sealed class ShipCutterPickTest
{
    private static readonly PoolSettings Settings = new() { FailureLogLevel = LogLevel.Error };

    [Test]
    public async Task ClickPicksSeamOrWall()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;

        Entity<MapGridComponent> grid = default;
        MapId mapId = default;
        EntityUid wall = default;
        ShipCutterComponent cutter = default!;

        await server.WaitPost(() =>
        {
            var tileMan = server.ResolveDependency<ITileDefinitionManager>();
            var tile = new Tile(tileMan["FloorSteel"].TileId);
            server.System<SharedMapSystem>().CreateMap(out mapId);
            grid = server.MapMan.CreateGridEntity(mapId);

            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), tile));
                }
            }

            server.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);

            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 1.5f, 1.5f));
            var tool = entMan.SpawnEntity("ShipTool", new EntityCoordinates(grid, 1.5f, 0.5f));
            cutter = entMan.GetComponent<ShipCutterComponent>(tool);
        });

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<SharedShipCutterSystem>();

            ShipCutterPick PickAt(float x, float y) =>
                system.Pick(new MapCoordinates(Vector2.Transform(new Vector2(x, y), entMan.System<SharedTransformSystem>().GetWorldMatrix(grid)), mapId), cutter);

            // Middle of the wall.
            var pick = PickAt(1.5f, 1.5f);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Wall));
            Assert.That(pick.Wall, Is.EqualTo(wall));

            // 0.12 from the edge of the wall tile: wider than the wall strip, so still the wall.
            Assert.That(PickAt(1.12f, 1.5f).Kind, Is.EqualTo(ShipCutterPickKind.Wall));

            // 0.05 from the inner edge of the wall tile: the seam under the wall.
            pick = PickAt(1.05f, 1.5f);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Seam));
            Assert.That(pick.Tile, Is.EqualTo(new Vector2i(1, 1)));
            Assert.That(pick.Edge, Is.EqualTo(CutEdges.West));
            Assert.That(pick.CanCut, Is.True);

            // The outer edge of the grid under the wall tile can not be cut, but the wall is there to be cut.
            Assert.That(PickAt(1.5f, 1.95f).Kind, Is.EqualTo(ShipCutterPickKind.Seam), "inner edge of tile (1,1) towards (1,2)");
            Assert.That(PickAt(2.95f, 0.5f).Kind, Is.EqualTo(ShipCutterPickKind.Seam));
            Assert.That(PickAt(2.95f, 0.5f).CanCut, Is.False, "the edge faces space");

            // Floor without a wall: the wider strip.
            pick = PickAt(0.5f, 0.14f);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Seam));
            Assert.That(pick.Edge, Is.EqualTo(CutEdges.South));
            Assert.That(PickAt(0.5f, 0.5f).Kind, Is.EqualTo(ShipCutterPickKind.None));
        });

        // An already cut seam is still the seam, but nothing to cut.
        await server.WaitPost(() =>
        {
            entMan.System<GridCutsSystem>().AddCut(grid, new Vector2i(0, 0), CutEdges.North);
        });

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<SharedShipCutterSystem>();
            var pos = Vector2.Transform(new Vector2(0.5f, 0.95f), entMan.System<SharedTransformSystem>().GetWorldMatrix(grid));
            var pick = system.Pick(new MapCoordinates(pos, mapId), cutter);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Seam));
            Assert.That(pick.CanCut, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
