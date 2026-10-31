using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    private readonly record struct BspLeaf(int X, int Y, int W, int H);

    /// <summary>
    /// <see cref="BspRoomDunGen"/>
    /// </summary>
    private async Task<Dungeon> GenerateBspRoomDunGen(
        Vector2i position,
        BspRoomDunGen gen,
        HashSet<Vector2i> reservedTiles,
        Random random)
    {
        var dungeon = new Dungeon();
        var tileDef = (ContentTileDefinition) _tileDefManager[gen.Tile];

        var leaves = new List<BspLeaf>();
        BspSplit(gen, position.X - gen.Size.X / 2, position.Y - gen.Size.Y / 2, gen.Size.X, gen.Size.Y, random, leaves, 0);

        var padding = gen.Padding;
        var setTiles = new List<(Vector2i, Tile)>();

        foreach (var leaf in leaves)
        {
            var availW = leaf.W - 2 * padding;
            var availH = leaf.H - 2 * padding;

            if (availW < gen.MinRoom.X || availH < gen.MinRoom.Y)
                continue;

            var roomW = random.Next(gen.MinRoom.X, Math.Min(gen.MaxRoom.X, availW) + 1);
            var roomH = random.Next(gen.MinRoom.Y, Math.Min(gen.MaxRoom.Y, availH) + 1);
            var roomX = leaf.X + padding + random.Next(0, availW - roomW + 1);
            var roomY = leaf.Y + padding + random.Next(0, availH - roomH + 1);

            var roomTiles = new HashSet<Vector2i>(roomW * roomH);
            var blocked = false;

            for (var x = 0; x < roomW && !blocked; x++)
            {
                for (var y = 0; y < roomH; y++)
                {
                    var tile = new Vector2i(roomX + x, roomY + y);

                    if (reservedTiles.Contains(tile))
                    {
                        blocked = true;
                        break;
                    }

                    roomTiles.Add(tile);
                }
            }

            if (blocked)
                continue;

            // Same exterior ring as prefab rooms: one tile around, corners included.
            var exterior = new HashSet<Vector2i>(roomW * 2 + roomH * 2 + 4);

            for (var x = -1; x <= roomW; x++)
            {
                for (var y = -1; y <= roomH; y++)
                {
                    if (x != -1 && y != -1 && x != roomW && y != roomH)
                        continue;

                    var tile = new Vector2i(roomX + x, roomY + y);

                    if (!reservedTiles.Contains(tile))
                        exterior.Add(tile);
                }
            }

            foreach (var tile in roomTiles)
            {
                setTiles.Add((tile, _tile.GetVariantTile(tileDef, random)));
            }

            var bounds = new Box2i(roomX, roomY, roomX + roomW - 1, roomY + roomH - 1);
            var center = new Vector2(roomX + roomW / 2f, roomY + roomH / 2f);
            dungeon.AddRoom(new DungeonRoom(roomTiles, center, bounds, exterior));

            await SuspendDungeon();

            if (!ValidateResume())
                return Dungeon.Empty;
        }

        _maps.SetTiles(_gridUid, _grid, setTiles);

        foreach (var room in dungeon.Rooms)
        {
            SetDungeonEntrance(dungeon, room, reservedTiles, random);
        }

        dungeon.Rebuild();
        return dungeon;
    }

    private void BspSplit(BspRoomDunGen gen, int x, int y, int w, int h, Random random, List<BspLeaf> leaves, int depth)
    {
        var minW = gen.MinRoom.X + 2 * gen.Padding;
        var minH = gen.MinRoom.Y + 2 * gen.Padding;
        var maxLeafW = gen.MaxRoom.X + 2 * gen.Padding;
        var maxLeafH = gen.MaxRoom.Y + 2 * gen.Padding;

        // A cut across X needs room for two leaves of at least minW, same for Y.
        var canCutX = w >= 2 * minW;
        var canCutY = h >= 2 * minH;
        var tooBig = w > maxLeafW || h > maxLeafH;

        if ((!canCutX && !canCutY) ||
            depth >= 12 ||
            (!tooBig && !random.Prob(gen.SplitChance)))
        {
            leaves.Add(new BspLeaf(x, y, w, h));
            return;
        }

        bool cutX;

        if (canCutX && canCutY)
        {
            // Cut the long side so leaves stay roughly square.
            if (w > h * 1.25f)
                cutX = true;
            else if (h > w * 1.25f)
                cutX = false;
            else
                cutX = random.Prob(0.5f);
        }
        else
        {
            cutX = canCutX;
        }

        if (cutX)
        {
            var cut = random.Next(minW, w - minW + 1);
            BspSplit(gen, x, y, cut, h, random, leaves, depth + 1);
            BspSplit(gen, x + cut, y, w - cut, h, random, leaves, depth + 1);
        }
        else
        {
            var cut = random.Next(minH, h - minH + 1);
            BspSplit(gen, x, y, w, cut, random, leaves, depth + 1);
            BspSplit(gen, x, y + cut, w, h - cut, random, leaves, depth + 1);
        }
    }
}
