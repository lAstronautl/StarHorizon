using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared.Decals;
using Content.Shared.Procedural;
using Content.Shared.Procedural.Furnish;
using Content.Shared.Procedural.PostGeneration;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    private static readonly Vector2i[] FurnishCardinals =
    {
        new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
    };

    /// <summary>
    /// Per-room scratch state for <see cref="RoomFurnishDunGen"/>.
    /// </summary>
    private sealed class FurnishContext
    {
        public required DungeonRoom Room;
        public required List<Vector2i> TileList;
        public readonly HashSet<Vector2i> Doors = new();
        /// <summary> Room tiles directly next to a doorway. Always kept walkable. </summary>
        public readonly HashSet<Vector2i> DoorSeeds = new();
        public readonly HashSet<Vector2i> Blocked = new();
        public readonly HashSet<Vector2i> Reserved = new();
        /// <summary> Every tile already holding a member of some group (floor and wall tiles alike). </summary>
        public readonly HashSet<Vector2i> Used = new();
        public readonly Dictionary<Vector2i, bool> WallCache = new();
        public HashSet<Vector2i> Reach = new();
    }

    /// <summary>
    /// <see cref="RoomFurnishDunGen"/>
    /// </summary>
    private async Task PostGen(RoomFurnishDunGen gen, Dungeon dungeon, HashSet<Vector2i> reservedTiles, Random random)
    {
        if (dungeon.Rooms.Count == 0)
            return;

        var pool = gen.Archetypes
            .Select(a => _prototype.Index(a))
            .OrderBy(a => a.ID, StringComparer.Ordinal)
            .ToList();
        var fallback = gen.Fallback == null ? null : _prototype.Index(gen.Fallback.Value);

        AssignArchetypes(gen, dungeon, pool, fallback, random);

        foreach (var room in dungeon.Rooms)
        {
            if (room.Archetype == null)
                continue;

            await FurnishRoom(room, room.Archetype, random);

            await SuspendDungeon();
            if (!ValidateResume())
                return;
        }
    }

    #region Role assignment

    private void AssignArchetypes(
        RoomFurnishDunGen gen,
        Dungeon dungeon,
        List<RoomArchetypePrototype> pool,
        RoomArchetypePrototype? fallback,
        Random random)
    {
        var rooms = dungeon.Rooms;

        // Depth: normalised distance from the origin (the ship lands near it) so far rooms can be "objective" rooms.
        var origin = new Vector2(gen.Origin.X, gen.Origin.Y);
        var distances = rooms.Select(r => Vector2.Distance(r.Center, origin)).ToList();
        var minDist = distances.Min();
        var maxDist = distances.Max();
        var depths = distances
            .Select(d => maxDist - minDist < 0.01f ? 0f : (d - minDist) / (maxDist - minDist))
            .ToList();

        var counts = new Dictionary<string, int>();

        bool Eligible(RoomArchetypePrototype arch, int index)
        {
            var room = rooms[index];

            return room.Archetype == null
                   && room.Tiles.Count >= arch.MinTiles && room.Tiles.Count <= arch.MaxTiles
                   && room.Entrances.Count >= arch.MinDoors && room.Entrances.Count <= arch.MaxDoors
                   && depths[index] >= arch.MinDepth && depths[index] <= arch.MaxDepth
                   && counts.GetValueOrDefault(arch.ID) < arch.MaxPerDungeon;
        }

        void Assign(RoomArchetypePrototype arch, int index)
        {
            rooms[index].Archetype = arch;
            counts[arch.ID] = counts.GetValueOrDefault(arch.ID) + 1;
        }

        // 1. Guaranteed roles first (objective room, etc).
        foreach (var arch in pool.Where(a => a.MinPerDungeon > 0))
        {
            for (var i = 0; i < arch.MinPerDungeon; i++)
            {
                var candidates = Enumerable.Range(0, rooms.Count).Where(r => Eligible(arch, r)).ToList();

                if (candidates.Count == 0)
                {
                    _sawmill.Debug($"No suitable room for guaranteed archetype {arch.ID}");
                    break;
                }

                Assign(arch, candidates[random.Next(candidates.Count)]);
            }
        }

        // 2. Everything else by weight.
        for (var i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].Archetype != null)
                continue;

            var candidates = pool.Where(a => Eligible(a, i)).ToList();

            if (candidates.Count == 0)
            {
                if (fallback != null)
                    Assign(fallback, i);

                continue;
            }

            var total = candidates.Sum(a => a.Weight);
            var roll = random.NextFloat() * total;
            var picked = candidates[^1];

            foreach (var arch in candidates)
            {
                roll -= arch.Weight;

                if (roll > 0f)
                    continue;

                picked = arch;
                break;
            }

            Assign(picked, i);
        }
    }

    #endregion

    #region Furnishing

    private async Task FurnishRoom(DungeonRoom room, RoomArchetypePrototype arch, Random random)
    {
        var ctx = new FurnishContext
        {
            Room = room,
            // Sorted so the result only depends on the seed, not on HashSet ordering.
            TileList = room.Tiles.OrderBy(t => t.X).ThenBy(t => t.Y).ToList(),
        };

        foreach (var door in room.Entrances)
        {
            ctx.Doors.Add(door);

            foreach (var dir in FurnishCardinals)
            {
                var next = door + dir;

                if (room.Tiles.Contains(next))
                    ctx.DoorSeeds.Add(next);
            }
        }

        foreach (var tile in ctx.TileList)
        {
            // Walls inside the room, pillars, pre-placed machines.
            if (FurnishIsWall(ctx, tile) ||
                !_anchorable.TileFree((_gridUid, _grid), tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
            {
                ctx.Blocked.Add(tile);
            }
        }

        // Doorways must stay usable.
        ctx.Reserved.UnionWith(ctx.DoorSeeds);
        ctx.Reach = FurnishFlood(ctx, ctx.Blocked);

        // Alternatives: of all entries sharing an id only one is built.
        var chosenAlternatives = new Dictionary<string, FurnishEntry>();
        var alternativeCount = new Dictionary<string, int>();

        foreach (var entry in arch.Entries)
        {
            if (entry.Alternative == null)
                continue;

            var seen = alternativeCount.GetValueOrDefault(entry.Alternative) + 1;
            alternativeCount[entry.Alternative] = seen;

            // Reservoir sampling keeps the pick uniform without a second pass.
            if (random.Next(seen) == 0)
                chosenAlternatives[entry.Alternative] = entry;
        }

        foreach (var entry in arch.Entries)
        {
            if (entry.Alternative != null && chosenAlternatives[entry.Alternative] != entry)
                continue;

            await FurnishEntryRun(ctx, arch.ID, entry, random);

            if (!ValidateResume())
                return;
        }

        // "What happened here" layer.
        RoomStory? story = null;

        if (arch.Stories.Count > 0 && random.Prob(arch.StoryChance))
        {
            var total = arch.Stories.Sum(x => x.Weight);
            var roll = random.NextFloat() * total;
            story = arch.Stories[^1];

            foreach (var candidate in arch.Stories)
            {
                roll -= candidate.Weight;

                if (roll > 0f)
                    continue;

                story = candidate;
                break;
            }

            foreach (var entry in story.Entries)
            {
                await FurnishEntryRun(ctx, $"{arch.ID}/{story.Id}", entry, random);

                if (!ValidateResume())
                    return;
            }
        }

        ApplyFurnishDecals(ctx, arch.Decals, random);

        if (story != null)
            ApplyFurnishDecals(ctx, story.Decals, random);
    }

    private async Task FurnishEntryRun(FurnishContext ctx, string owner, FurnishEntry entry, Random random)
    {
        var count = random.Next(entry.Min, Math.Max(entry.Min, entry.Max) + 1);

        if (entry.PerTiles > 0)
            count += ctx.Room.Tiles.Count / entry.PerTiles;

        if (entry.Min <= 0 && !random.Prob(entry.Chance))
            count = 0;

        if (count <= 0)
            return;

        var group = _prototype.Index(entry.Group);
        var placed = 0;

        for (var i = 0; i < count; i++)
        {
            if (!TryPlaceGroup(ctx, group, entry.Bias, random))
                break;

            placed++;
            await SuspendDungeon();

            if (!ValidateResume())
                return;
        }

        if (placed < entry.Min)
            _sawmill.Debug($"Room {owner}: placed {placed}/{entry.Min} of {group.ID}");
    }

    private void AddFurnishDecal(string id, Vector2i tile, Color? tint, Angle rotation, bool cleanable = false)
    {
        if (!_prototype.HasIndex<DecalPrototype>(id))
        {
            _sawmill.Error($"Unknown decal {id} in room archetype");
            return;
        }

        // Decals are anchored at their corner, not the tile center.
        var coords = _maps.GridTileToLocal(_gridUid, _grid, tile).Offset(-_grid.TileSizeHalfVector);
        _decals.TryAddDecal(id, coords, out _, color: tint, rotation: rotation, cleanable: cleanable);
    }

    private void ApplyFurnishDecals(FurnishContext ctx, List<FurnishDecalRule> rules, Random random)
    {
        foreach (var rule in rules)
        {
            switch (rule.Mode)
            {
                case FurnishDecalMode.Scatter:
                case FurnishDecalMode.Fill:
                    if (rule.Decals.Count == 0)
                        break;

                    foreach (var tile in ctx.TileList)
                    {
                        if (rule.Mode == FurnishDecalMode.Scatter && !random.Prob(rule.Chance))
                            continue;

                        var rotation = rule.RandomRotation ? new Angle(random.NextDouble() * Math.Tau) : Angle.Zero;
                        AddFurnishDecal(rule.Decals[random.Next(rule.Decals.Count)], tile, rule.Tint, rotation, rule.Cleanable);
                    }

                    break;

                case FurnishDecalMode.Skirting:
                    foreach (var tile in ctx.TileList)
                    {
                        DirectionFlag flags = DirectionFlag.None;
                        var count = 0;

                        for (var i = 0; i < 4; i++)
                        {
                            var dir = (DirectionFlag) Math.Pow(2, i);

                            if (!FurnishIsWall(ctx, tile + dir.AsDir().ToIntVec()))
                                continue;

                            flags |= dir;
                            count++;
                        }

                        if (count == 1 && rule.Cardinal.TryGetValue(flags, out var cardinal))
                            AddFurnishDecal(cardinal, tile, rule.Tint, Angle.Zero);
                        else if (count == 2 && rule.Corner.TryGetValue(flags, out var corner))
                            AddFurnishDecal(corner, tile, rule.Tint, Angle.Zero);
                    }

                    break;

                case FurnishDecalMode.DoorMarks:
                    foreach (var door in ctx.Room.Entrances)
                    {
                        foreach (var seed in ctx.DoorSeeds)
                        {
                            var delta = door - seed;

                            if (Math.Abs(delta.X) + Math.Abs(delta.Y) != 1)
                                continue;

                            // Side of the tile the door is on.
                            var side = delta.Y < 0 ? DirectionFlag.South
                                : delta.Y > 0 ? DirectionFlag.North
                                : delta.X > 0 ? DirectionFlag.East
                                : DirectionFlag.West;

                            if (rule.Cardinal.TryGetValue(side, out var mark))
                                AddFurnishDecal(mark, seed, rule.Tint, Angle.Zero);
                        }
                    }

                    break;
            }
        }
    }

    private bool FurnishIsWall(FurnishContext ctx, Vector2i tile)
    {
        if (ctx.Doors.Contains(tile))
            return false;

        if (!ctx.Room.Tiles.Contains(tile))
            return true;

        if (ctx.WallCache.TryGetValue(tile, out var cached))
            return cached;

        var wall = HasWall(tile);
        ctx.WallCache[tile] = wall;
        return wall;
    }

    /// <summary>
    /// Flood fill over the room's walkable tiles starting from doorways.
    /// With no doorways nothing can be proven, so the whole room counts as reachable.
    /// </summary>
    private HashSet<Vector2i> FurnishFlood(FurnishContext ctx, HashSet<Vector2i> blocked)
    {
        var reached = new HashSet<Vector2i>();

        if (ctx.DoorSeeds.Count == 0)
            return reached;

        var open = new Stack<Vector2i>();

        foreach (var seed in ctx.DoorSeeds)
        {
            if (!blocked.Contains(seed) && reached.Add(seed))
                open.Push(seed);
        }

        while (open.Count > 0)
        {
            var current = open.Pop();

            foreach (var dir in FurnishCardinals)
            {
                var next = current + dir;

                if (!ctx.Room.Tiles.Contains(next) || blocked.Contains(next) || !reached.Add(next))
                    continue;

                open.Push(next);
            }
        }

        return reached;
    }

    /// <summary>
    /// Facing directions (pointing into the room) at which <paramref name="group"/> may be anchored on <paramref name="tile"/>.
    /// </summary>
    private IEnumerable<Vector2i> FurnishFacings(FurnishContext ctx, FurnitureGroupPrototype group, Vector2i tile)
    {
        var walls = new List<Vector2i>(4);

        foreach (var dir in FurnishCardinals)
        {
            if (FurnishIsWall(ctx, tile + dir))
                walls.Add(dir);
        }

        switch (group.Placement)
        {
            case FurnishPlacement.Wall:
                foreach (var wall in walls)
                    yield return -wall;
                break;

            case FurnishPlacement.Corner:
                foreach (var wall in walls)
                {
                    // Needs a second wall at a right angle to the first.
                    if (walls.Any(w => w.X * wall.X + w.Y * wall.Y == 0))
                        yield return -wall;
                }
                break;

            case FurnishPlacement.DoorSide:
                if (walls.Count == 0)
                    yield break;

                var near = false;

                foreach (var seed in ctx.DoorSeeds)
                {
                    if (Math.Abs(seed.X - tile.X) <= 2 && Math.Abs(seed.Y - tile.Y) <= 2)
                    {
                        near = true;
                        break;
                    }
                }

                if (!near)
                    yield break;

                foreach (var wall in walls)
                    yield return -wall;
                break;

            case FurnishPlacement.Center:
                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        if ((x != 0 || y != 0) && FurnishIsWall(ctx, tile + new Vector2i(x, y)))
                            yield break;
                    }
                }

                foreach (var dir in FurnishCardinals)
                    yield return dir;
                break;

            default:
                // Free: facing does not matter much, one candidate per tile keeps the search small.
                yield return FurnishCardinals[0];
                break;
        }
    }

    private bool TryPlaceGroup(FurnishContext ctx, FurnitureGroupPrototype group, FurnishBias bias, Random random)
    {
        // Optional members are decided once, so every candidate spot is judged on the same shape.
        var members = group.Members.Where(m => m.Chance >= 1f || random.Prob(m.Chance)).ToList();

        if (members.Count == 0)
            return false;

        var candidates = new List<(Vector2i Tile, Vector2i Facing)>();

        foreach (var tile in ctx.TileList)
        {
            if (ctx.Blocked.Contains(tile) || ctx.Reserved.Contains(tile))
                continue;

            if (group.Placement == FurnishPlacement.Free && ctx.Used.Contains(tile))
                continue;

            foreach (var facing in FurnishFacings(ctx, group, tile))
            {
                candidates.Add((tile, facing));
            }
        }

        random.Shuffle(candidates);

        // Brogue-style "near / far from origin": sort by distance to the nearest doorway. OrderBy is stable,
        // so the shuffle above still breaks ties randomly.
        if (bias != FurnishBias.None && ctx.DoorSeeds.Count > 0)
        {
            int DoorDistance(Vector2i tile)
            {
                var best = int.MaxValue;

                foreach (var seed in ctx.DoorSeeds)
                {
                    best = Math.Min(best, Math.Abs(seed.X - tile.X) + Math.Abs(seed.Y - tile.Y));
                }

                return best;
            }

            candidates = bias == FurnishBias.NearDoor
                ? candidates.OrderBy(c => DoorDistance(c.Tile)).ToList()
                : candidates.OrderByDescending(c => DoorDistance(c.Tile)).ToList();
        }

        const int maxAttempts = 200;
        var attempts = 0;

        foreach (var (anchor, facing) in candidates)
        {
            if (attempts++ >= maxAttempts)
                break;

            // Local frame: facing = away from the wall, right = along the wall.
            var right = new Vector2i(facing.Y, -facing.X);
            var positions = new List<Vector2i>(members.Count);
            var footprint = new HashSet<Vector2i>();
            var newBlocked = new HashSet<Vector2i>();
            var ok = true;

            foreach (var member in members)
            {
                var pos = anchor + right * member.Offset.X + facing * member.Offset.Y;
                positions.Add(pos);

                if (member.OnWall)
                {
                    // Wall decor: must be an unused wall tile on the outline of this room.
                    if (ctx.Room.Tiles.Contains(pos) || !ctx.Room.Exterior.Contains(pos) ||
                        !FurnishIsWall(ctx, pos) || ctx.Used.Contains(pos))
                    {
                        ok = false;
                        break;
                    }

                    continue;
                }

                footprint.Add(pos);

                if (!ctx.Room.Tiles.Contains(pos) || ctx.Used.Contains(pos))
                {
                    ok = false;
                    break;
                }

                // Solid furniture respects reserved clearance, small items only keep doorways free.
                if (ctx.Blocked.Contains(pos) ||
                    (member.Blocking ? ctx.Reserved.Contains(pos) : ctx.DoorSeeds.Contains(pos)))
                {
                    ok = false;
                    break;
                }

                if (member.Blocking)
                    newBlocked.Add(pos);
            }

            if (!ok)
                continue;

            // Free space in front of the furniture so it can actually be used.
            var clearance = new HashSet<Vector2i>();

            for (var i = 0; i < members.Count && ok; i++)
            {
                if (!members[i].Blocking || members[i].OnWall)
                    continue;

                for (var k = 1; k <= group.Clearance; k++)
                {
                    var tile = positions[i] + facing * k;

                    if (footprint.Contains(tile))
                        continue;

                    if (!ctx.Room.Tiles.Contains(tile) || ctx.Blocked.Contains(tile))
                    {
                        ok = false;
                        break;
                    }

                    clearance.Add(tile);
                }
            }

            if (!ok)
                continue;

            // Never cut off any part of the room that could be walked to before.
            var allBlocked = new HashSet<Vector2i>(ctx.Blocked);
            allBlocked.UnionWith(newBlocked);
            HashSet<Vector2i>? reach = null;

            if (ctx.DoorSeeds.Count > 0)
            {
                reach = FurnishFlood(ctx, allBlocked);

                foreach (var tile in ctx.Reach)
                {
                    if (!newBlocked.Contains(tile) && !reach.Contains(tile))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok)
                continue;

            // Commit.
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var coords = _maps.GridTileToLocal(_gridUid, _grid, positions[i]);
                var uid = _entManager.SpawnAtPosition(member.Proto, coords);

                if (member.Rotate)
                    _transform.SetLocalRotation(uid, new Vector2(facing.X, facing.Y).ToWorldAngle());

                if (member.OnTop != null)
                {
                    var items = _entTable.GetSpawns(_prototype.Index(member.OnTop.Value), random);
                    _entManager.SpawnEntitiesAttachedTo(coords, items);
                }
            }

            ctx.Blocked.UnionWith(newBlocked);
            ctx.Reserved.UnionWith(clearance);
            ctx.Used.UnionWith(positions);

            if (reach != null)
                ctx.Reach = reach;

            return true;
        }

        return false;
    }

    #endregion
}
