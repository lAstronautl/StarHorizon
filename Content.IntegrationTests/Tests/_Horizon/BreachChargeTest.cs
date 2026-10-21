using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Horizon;

/// <summary>
/// A breaching charge cuts a line of seams along the edge it hangs on and breaks nothing.
/// </summary>
[TestFixture]
public sealed class BreachChargeTest
{
    private static readonly PoolSettings Settings = new() { FailureLogLevel = LogLevel.Error };

    private static Entity<MapGridComponent> MakeGrid(TestPair pair, int width, int height, out MapId mapId)
    {
        var server = pair.Server;
        var tileMan = server.ResolveDependency<ITileDefinitionManager>();
        var tile = new Tile(tileMan["FloorSteel"].TileId);

        server.System<SharedMapSystem>().CreateMap(out mapId);
        var grid = server.MapMan.CreateGridEntity(mapId);

        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                tiles.Add((new Vector2i(x, y), tile));
            }
        }

        server.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
        return grid;
    }

    private static void HangCharge(TestPair pair, Entity<MapGridComponent> grid, Vector2i tile, CutEdges edge)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        // In the middle of the edge, as the system hangs it.
        var centre = new System.Numerics.Vector2(tile.X + 0.5f, tile.Y + 0.5f) +
                     new System.Numerics.Vector2(edge.Offset().X, edge.Offset().Y) * 0.5f;
        var charge = entMan.SpawnEntity("BreachChargeArmed", new EntityCoordinates(grid, centre));

        var armed = entMan.GetComponent<ArmedBreachChargeComponent>(charge);
        armed.Grid = grid;
        armed.Tile = tile;
        armed.Edge = edge;
        armed.Segments = 5;
        entMan.System<TriggerSystem>().Trigger(charge, key: "timer");
    }

    [Test]
    public async Task CutsFiveSeamsAndLeavesTheWall()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;

        Entity<MapGridComponent> grid = default;
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            grid = MakeGrid(pair, 7, 7, out _);

            // A wall right on the line the charge will cut.
            wall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 3.5f, 3.5f));
            HangCharge(pair, grid, new Vector2i(3, 3), CutEdges.East);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var cuts = entMan.GetComponent<GridCutsComponent>(grid).Cuts;

            // The vertical seam east of x=3, five tiles centred on y=3: y 1..5.
            for (var y = 0; y < 7; y++)
            {
                var cut = cuts.GetValueOrDefault(new Vector2i(3, y)).HasFlag(CutEdges.East);
                Assert.That(cut, Is.EqualTo(y is >= 1 and <= 5), $"seam at y={y}");
            }

            Assert.That(entMan.Deleted(wall), Is.False, "the wall is not broken");
            Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "the wall stays where it is");
            Assert.That(cuts.GetValueOrDefault(new Vector2i(3, 3)).HasFlag(CutEdges.East), Is.True, "the gap is also under the wall");

            Assert.That(entMan.EntityQuery<ArmedBreachChargeComponent>().Any(), Is.False, "the charge is used up");
            Assert.That(server.MapMan.GetAllGrids(entMan.GetComponent<TransformComponent>(grid).MapID).Count(), Is.EqualTo(1), "five seams do not cut a seven tile grid in two");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CuttingTheWholeGridSplitsIt()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;

        Entity<MapGridComponent> grid = default;
        MapId mapId = default;

        await server.WaitPost(() =>
        {
            // Five tiles high: the line of five seams cuts from one side to the other.
            grid = MakeGrid(pair, 7, 5, out mapId);
            HangCharge(pair, grid, new Vector2i(3, 2), CutEdges.East);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.MapMan.GetAllGrids(mapId).Count(), Is.EqualTo(2), "the grid is cut in two");
        });

        await pair.CleanReturnAsync();
    }
}
