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
    /// One cell of the BSP partition. Without corridors a leaf holds a room of <c>(W-1) x (H-1)</c> tiles anchored at its
    /// lower-left corner, so neighbours are separated by one shared wall tile. With corridors the room fills the whole
    /// cell and the cells are separated by corridor bands.
    /// </summary>
    private sealed class BspNode
    {
        public int X, Y, W, H;
        public int Shrink; // 1 = shared wall mode, 0 = corridor mode
        public BspNode? A, B;
        public bool CutX;
        public int Line; // first coordinate of the cut: shared-wall mode = first tile of B, corridor mode = first tile of the band
        public DungeonRoom? Room;

        public bool IsLeaf => A == null;

        public int RoomX => X;
        public int RoomY => Y;
        public int RoomW => W - Shrink;
        public int RoomH => H - Shrink;
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
        var corridorWidth = Math.Max(0, gen.CorridorWidth);

        var areaX = position.X - gen.Size.X / 2;
        var areaY = position.Y - gen.Size.Y / 2;
        var root = BspBuild(gen, areaX, areaY, gen.Size.X, gen.Size.Y, random, 0, corridorWidth);
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

            // Same exterior ring as prefab rooms: one tile around, corners included.
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

        if (corridorWidth > 0)
        {
            // Hallways along every split, branching off each other like a station's corridor network.
            var corridor = new HashSet<Vector2i>();
            BspCarve(root, areaX, areaY, gen.Size.X, gen.Size.Y, corridorWidth, corridor);
            corridor.ExceptWith(dungeon.RoomTiles);
            corridor.ExceptWith(reservedTiles);

            foreach (var tile in corridor)
            {
                setTiles.Add((tile, _tile.GetVariantTile(tileDef, random)));
            }

            _maps.SetTiles(_gridUid, _grid, setTiles);
            dungeon.CorridorTiles.UnionWith(corridor);

            // Every room opens onto a corridor: a door in its wall next to a corridor tile, sometimes a second one.
            foreach (var leaf in leaves)
            {
                if (leaf.Room != null)
                    BspAddCorridorDoors(leaf, corridor, random);
            }
        }
        else
        {
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
        }

        // A lone room (no neighbours) still needs a way in.
        foreach (var room in dungeon.Rooms)
        {
            if (room.Entrances.Count == 0)
                SetDungeonEntrance(dungeon, room, reservedTiles, random);
        }

        dungeon.Rebuild();

        if (corridorWidth > 0)
        {
            dungeon.RefreshAllTiles();
            BuildCorridorExterior(dungeon);
        }

        return dungeon;
    }

    private BspNode BspBuild(BspRoomDunGen gen, int x, int y, int w, int h, Random random, int depth, int corridorWidth)
    {
        var shrink = corridorWidth > 0 ? 0 : 1;
        // Corridor band: the rooms' exterior rings on both sides plus the corridor itself.
        var gap = corridorWidth > 0 ? corridorWidth + 2 : 0;
        var node = new BspNode { X = x, Y = y, W = w, H = h, Shrink = shrink };

        var minW = gen.MinRoom.X + shrink;
        var minH = gen.MinRoom.Y + shrink;
        var maxLeafW = gen.MaxRoom.X + shrink;
        var maxLeafH = gen.MaxRoom.Y + shrink;

        var canCutX = w >= 2 * minW + gap;
        var canCutY = h >= 2 * minH + gap;
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
            var cut = random.Next(minW, w - gap - minW + 1);
            node.Line = x + cut;
            node.A = BspBuild(gen, x, y, cut, h, random, depth + 1, corridorWidth);
            node.B = BspBuild(gen, x + cut + gap, y, w - cut - gap, h, random, depth + 1, corridorWidth);
        }
        else
        {
            var cut = random.Next(minH, h - gap - minH + 1);
            node.Line = y + cut;
            node.A = BspBuild(gen, x, y, w, cut, random, depth + 1, corridorWidth);
            node.B = BspBuild(gen, x, y + cut + gap, w, h - cut - gap, random, depth + 1, corridorWidth);
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
    /// Carves the corridor of every split. A band is [ring][corridor x width][ring]; the corridor runs the whole
    /// length of the cell and one tile past each end that touches another band, so branches join the hallway they end at.
    /// Ends on the outer boundary stay closed (walls are added later).
    /// </summary>
    private static void BspCarve(BspNode node, int areaX, int areaY, int areaW, int areaH, int width, HashSet<Vector2i> corridor)
    {
        if (node.IsLeaf)
            return;

        if (node.CutX)
        {
            var from = node.Y > areaY ? node.Y - 1 : node.Y;
            var to = node.Y + node.H < areaY + areaH ? node.Y + node.H : node.Y + node.H - 1;

            for (var cx = node.Line + 1; cx <= node.Line + width; cx++)
            {
                for (var y = from; y <= to; y++)
                {
                    corridor.Add(new Vector2i(cx, y));
                }
            }
        }
        else
        {
            var from = node.X > areaX ? node.X - 1 : node.X;
            var to = node.X + node.W < areaX + areaW ? node.X + node.W : node.X + node.W - 1;

            for (var cy = node.Line + 1; cy <= node.Line + width; cy++)
            {
                for (var x = from; x <= to; x++)
                {
                    corridor.Add(new Vector2i(x, cy));
                }
            }
        }

        BspCarve(node.A!, areaX, areaY, areaW, areaH, width, corridor);
        BspCarve(node.B!, areaX, areaY, areaW, areaH, width, corridor);
    }

    /// <summary>
    /// Door(s) from a room onto an adjacent corridor tile (never at a ring corner).
    /// </summary>
    private void BspAddCorridorDoors(BspNode leaf, HashSet<Vector2i> corridor, Random random)
    {
        var room = leaf.Room!;
        var candidates = new List<Vector2i>();

        void Side(int fixedCoord, int from, int to, bool horizontalSide, int outward)
        {
            for (var i = from; i <= to; i++)
            {
                var door = horizontalSide ? new Vector2i(i, fixedCoord) : new Vector2i(fixedCoord, i);
                var beyond = horizontalSide ? new Vector2i(i, fixedCoord + outward) : new Vector2i(fixedCoord + outward, i);

                if (corridor.Contains(beyond))
                    candidates.Add(door);
            }
        }

        // Bottom, top, left, right ring tiles without the corners.
        Side(leaf.RoomY - 1, leaf.RoomX, leaf.RoomX + leaf.RoomW - 1, true, -1);
        Side(leaf.RoomY + leaf.RoomH, leaf.RoomX, leaf.RoomX + leaf.RoomW - 1, true, 1);
        Side(leaf.RoomX - 1, leaf.RoomY, leaf.RoomY + leaf.RoomH - 1, false, -1);
        Side(leaf.RoomX + leaf.RoomW, leaf.RoomY, leaf.RoomY + leaf.RoomH - 1, false, 1);

        if (candidates.Count == 0)
            return;

        var first = candidates[random.Next(candidates.Count)];
        room.Entrances.Add(first);

        // A second door a few tiles away, so bigger rooms are not dead ends.
        if (candidates.Count > 8 && random.Prob(0.5f))
        {
            var far = candidates.FindAll(c => Math.Abs(c.X - first.X) + Math.Abs(c.Y - first.Y) >= 4);

            if (far.Count > 0)
                room.Entrances.Add(far[random.Next(far.Count)]);
        }
    }

    /// <summary>
    /// Do the two leaves' rooms share a wall tile (exactly one tile apart) with an overlap of at least one tile?
    /// Returns the overlap range along the wall: [from, to). Shared-wall mode only.
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
