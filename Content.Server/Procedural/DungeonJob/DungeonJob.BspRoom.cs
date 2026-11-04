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
    /// <summary>
    /// One cell of the BSP partition. A leaf holds a room of <c>(W-1) x (H-1)</c> tiles anchored at its lower-left corner,
    /// so two neighbouring rooms are separated by exactly one tile that serves as their shared wall.
    /// </summary>
    private sealed class BspNode
    {
        public int X, Y, W, H;
        public BspNode? A, B;
        public bool CutX;
        public int Line; // coordinate of the cut (first tile of B)
        public DungeonRoom? Room;

        public bool IsLeaf => A == null;

        // Room rectangle: lower-left anchored, one tile narrower/shorter than the cell (the shared wall).
        public int RoomX => X;
        public int RoomY => Y;
        public int RoomW => W - 1;
        public int RoomH => H - 1;
    }

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

        var root = BspBuild(gen, position.X - gen.Size.X / 2, position.Y - gen.Size.Y / 2, gen.Size.X, gen.Size.Y, random, 0);
        var leaves = new List<BspNode>();
        BspLeaves(root, leaves);

        var setTiles = new List<(Vector2i, Tile)>();

        foreach (var leaf in leaves)
        {
            var roomTiles = new HashSet<Vector2i>(leaf.RoomW * leaf.RoomH);
            var blocked = false;

            for (var x = 0; x < leaf.RoomW && !blocked; x++)
            {
                for (var y = 0; y < leaf.RoomH; y++)
                {
                    var tile = new Vector2i(leaf.RoomX + x, leaf.RoomY + y);

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

            // Same exterior ring as prefab rooms: one tile around, corners included. Neighbouring rooms share it.
            var exterior = new HashSet<Vector2i>(leaf.RoomW * 2 + leaf.RoomH * 2 + 4);

            for (var x = -1; x <= leaf.RoomW; x++)
            {
                for (var y = -1; y <= leaf.RoomH; y++)
                {
                    if (x != -1 && y != -1 && x != leaf.RoomW && y != leaf.RoomH)
                        continue;

                    var tile = new Vector2i(leaf.RoomX + x, leaf.RoomY + y);

                    if (!reservedTiles.Contains(tile))
                        exterior.Add(tile);
                }
            }

            foreach (var tile in roomTiles)
            {
                setTiles.Add((tile, _tile.GetVariantTile(tileDef, random)));
            }

            var bounds = new Box2i(leaf.RoomX, leaf.RoomY, leaf.RoomX + leaf.RoomW - 1, leaf.RoomY + leaf.RoomH - 1);
            var center = new Vector2(leaf.RoomX + leaf.RoomW / 2f, leaf.RoomY + leaf.RoomH / 2f);
            leaf.Room = new DungeonRoom(roomTiles, center, bounds, exterior);
            dungeon.AddRoom(leaf.Room);

            await SuspendDungeon();

            if (!ValidateResume())
                return Dungeon.Empty;
        }

        _maps.SetTiles(_gridUid, _grid, setTiles);

        // Doors straight through the shared walls: one per BSP split, so every room is reachable...
        var connected = new HashSet<(DungeonRoom, DungeonRoom)>();
        BspConnect(root, random, connected);

        // ...plus a few extra doors between other neighbours so the complex has loops.
        for (var i = 0; i < leaves.Count; i++)
        {
            for (var j = i + 1; j < leaves.Count; j++)
            {
                if (leaves[i].Room == null || leaves[j].Room == null || !random.Prob(gen.LoopChance))
                    continue;

                if (connected.Contains((leaves[i].Room!, leaves[j].Room!)) || connected.Contains((leaves[j].Room!, leaves[i].Room!)))
                    continue;

                if (BspSharedWall(leaves[i], leaves[j], out var from, out var to) && to - from >= 3)
                    BspAddDoor(leaves[i], leaves[j], from + (to - from) / 2, connected);
            }
        }

        // A lone room (no neighbours) still needs a way in.
        foreach (var room in dungeon.Rooms)
        {
            if (room.Entrances.Count == 0)
                SetDungeonEntrance(dungeon, room, reservedTiles, random);
        }

        dungeon.Rebuild();
        return dungeon;
    }

    private BspNode BspBuild(BspRoomDunGen gen, int x, int y, int w, int h, Random random, int depth)
    {
        var node = new BspNode { X = x, Y = y, W = w, H = h };

        // A leaf cell is room size + the shared wall tile.
        var minW = gen.MinRoom.X + 1;
        var minH = gen.MinRoom.Y + 1;
        var maxLeafW = gen.MaxRoom.X + 1;
        var maxLeafH = gen.MaxRoom.Y + 1;

        var canCutX = w >= 2 * minW;
        var canCutY = h >= 2 * minH;
        var tooBig = w > maxLeafW || h > maxLeafH;

        if ((!canCutX && !canCutY) || depth >= 12 || (!tooBig && !random.Prob(gen.SplitChance)))
            return node;

        bool cutX;

        if (canCutX && canCutY)
        {
            // Cut the long side so cells stay roughly square.
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

        node.CutX = cutX;

        if (cutX)
        {
            var cut = random.Next(minW, w - minW + 1);
            node.Line = x + cut;
            node.A = BspBuild(gen, x, y, cut, h, random, depth + 1);
            node.B = BspBuild(gen, x + cut, y, w - cut, h, random, depth + 1);
        }
        else
        {
            var cut = random.Next(minH, h - minH + 1);
            node.Line = y + cut;
            node.A = BspBuild(gen, x, y, w, cut, random, depth + 1);
            node.B = BspBuild(gen, x, y + cut, w, h - cut, random, depth + 1);
        }

        return node;
    }

    private static void BspLeaves(BspNode node, List<BspNode> leaves)
    {
        if (node.IsLeaf)
        {
            leaves.Add(node);
            return;
        }

        BspLeaves(node.A!, leaves);
        BspLeaves(node.B!, leaves);
    }

    /// <summary>
    /// Do the two leaves' rooms share a wall tile (exactly one tile apart) with an overlap of at least one tile?
    /// Returns the overlap range along the wall: [from, to).
    /// </summary>
    private static bool BspSharedWall(BspNode a, BspNode b, out int from, out int to)
    {
        from = to = 0;

        // Vertical wall: a's right neighbour column is a.X + a.W - 1 == b.X - 1 (b starts right after the wall tile).
        if (a.X + a.W == b.X || b.X + b.W == a.X)
        {
            from = Math.Max(a.RoomY, b.RoomY);
            to = Math.Min(a.RoomY + a.RoomH, b.RoomY + b.RoomH);
            return to - from >= 1;
        }

        // Horizontal wall.
        if (a.Y + a.H == b.Y || b.Y + b.H == a.Y)
        {
            from = Math.Max(a.RoomX, b.RoomX);
            to = Math.Min(a.RoomX + a.RoomW, b.RoomX + b.RoomW);
            return to - from >= 1;
        }

        return false;
    }

    private void BspAddDoor(BspNode a, BspNode b, int along, HashSet<(DungeonRoom, DungeonRoom)> connected)
    {
        if (a.Room == null || b.Room == null)
            return;

        Vector2i door;

        if (a.X + a.W == b.X)
            door = new Vector2i(b.X - 1, along); // a is left of b
        else if (b.X + b.W == a.X)
            door = new Vector2i(a.X - 1, along);
        else if (a.Y + a.H == b.Y)
            door = new Vector2i(along, b.Y - 1); // a is below b
        else
            door = new Vector2i(along, a.Y - 1);

        // The door tile is in both rooms' exterior ring; both rooms know it (RoomEntrance/EntranceFlank spawn it once).
        if (!a.Room.Entrances.Contains(door))
            a.Room.Entrances.Add(door);

        if (!b.Room.Entrances.Contains(door))
            b.Room.Entrances.Add(door);

        connected.Add((a.Room, b.Room));
    }

    /// <summary>
    /// Connects the two halves of every split through the shared wall that has the longest overlap.
    /// </summary>
    private void BspConnect(BspNode node, Random random, HashSet<(DungeonRoom, DungeonRoom)> connected)
    {
        if (node.IsLeaf)
            return;

        BspConnect(node.A!, random, connected);
        BspConnect(node.B!, random, connected);

        var left = new List<BspNode>();
        var right = new List<BspNode>();
        BspLeaves(node.A!, left);
        BspLeaves(node.B!, right);

        BspNode? bestA = null, bestB = null;
        var bestFrom = 0;
        var bestTo = 0;

        foreach (var a in left)
        {
            if (a.Room == null)
                continue;

            foreach (var b in right)
            {
                if (b.Room == null || !BspSharedWall(a, b, out var from, out var to))
                    continue;

                if (bestA == null || to - from > bestTo - bestFrom)
                {
                    bestA = a;
                    bestB = b;
                    bestFrom = from;
                    bestTo = to;
                }
            }
        }

        if (bestA != null && bestB != null)
            BspAddDoor(bestA, bestB, bestFrom + (bestTo - bestFrom) / 2, connected);
    }
}
