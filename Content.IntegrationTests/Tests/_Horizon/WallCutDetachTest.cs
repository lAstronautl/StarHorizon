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
/// A wall comes loose once it is cut off from the walls next to it, one edge per cut.
/// </summary>
[TestFixture]
public sealed class WallCutDetachTest
{
    private static readonly PoolSettings Settings = new() { FailureLogLevel = LogLevel.Error };

    [Test]
    public async Task WallIsHeldUntilEveryEdgeIsCut()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;

        Entity<MapGridComponent> grid = default;
        MapId mapId = default;
        EntityUid left = default, middle = default, right = default;
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

            left = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 0.5f, 1.5f));
            middle = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 1.5f, 1.5f));
            right = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 2.5f, 1.5f));
            cutter = entMan.GetComponent<ShipCutterComponent>(entMan.SpawnEntity("ShipTool", new EntityCoordinates(grid, 1.5f, 0.5f)));
        });

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<SharedShipCutterSystem>();
            var xform = entMan.System<SharedTransformSystem>();

            ShipCutterPick PickAt(float x, float y) =>
                system.Pick(new MapCoordinates(Vector2.Transform(new Vector2(x, y), xform.GetWorldMatrix(grid)), mapId), cutter);

            // The middle wall is held on the left and on the right: the click picks the side it is closer to.
            var pick = PickAt(1.3f, 1.5f);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Wall));
            Assert.That(pick.Edge, Is.EqualTo(CutEdges.West));
            Assert.That(PickAt(1.7f, 1.5f).Edge, Is.EqualTo(CutEdges.East));

            var cutters = entMan.System<ShipCutterSystem>();
            cutters.CutWall(cutter, middle, CutEdges.West);

            Assert.That(entMan.GetComponent<TransformComponent>(middle).Anchored, Is.True, "still held on the right");
            Assert.That(entMan.GetComponent<WallCutsComponent>(left).Cuts, Is.EqualTo(CutEdges.East), "the cut is on the other wall too");
            Assert.That(PickAt(1.3f, 1.5f).Edge, Is.EqualTo(CutEdges.East), "only the right edge is left to cut");

            cutters.CutWall(cutter, middle, CutEdges.East);
            Assert.That(entMan.GetComponent<TransformComponent>(middle).Anchored, Is.True, "cut off from both sides, but a click is left");
            Assert.That(PickAt(1.5f, 1.5f).Edge, Is.EqualTo(CutEdges.None));
            cutters.CutWall(cutter, middle, CutEdges.None);
            Assert.That(entMan.GetComponent<TransformComponent>(middle).Anchored, Is.False, "the last click unscrews it");

            // The wall on the left has nothing holding it any more, a click just unscrews it.
            Assert.That(entMan.GetComponent<TransformComponent>(left).Anchored, Is.True);
            pick = PickAt(0.5f, 1.5f);
            Assert.That(pick.Kind, Is.EqualTo(ShipCutterPickKind.Wall));
            Assert.That(pick.Edge, Is.EqualTo(CutEdges.None));
            cutters.CutWall(cutter, left, CutEdges.None);
            Assert.That(entMan.GetComponent<TransformComponent>(left).Anchored, Is.False);
            Assert.That(entMan.GetComponent<TransformComponent>(right).Anchored, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
