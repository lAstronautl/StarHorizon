using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

/// <summary>
/// Horizon: hostile NPCs with NavSmash must break dynamic (unanchored) obstacles such as lockers
/// when those are the only way to their target. Otherwise a player can barricade himself with closets
/// and be completely safe from mobs.
/// </summary>
[TestFixture]
public sealed class NPCSmashDynamicObstacleTest
{
    private const int MaxSeconds = 90;

    [TestCase("LockerSteel")]
    [TestCase("ClosetEmergency")]
    public async Task HostileSmashesLockerBarricade(string obstacleProto)
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid target = default;
        EntityUid obstacle = default;
        EntityUid npc = default;

        await server.WaitPost(() =>
        {
            var mapSys = entMan.System<SharedMapSystem>();
            var grid = map.Grid;
            var tile = map.Tile.Tile;

            for (var x = -4; x <= 10; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }

            EntityCoordinates Coords(int x, int y) => new(grid.Owner, new Vector2(x + 0.5f, y + 0.5f));

            // Target is boxed in by walls on three sides and an obstacle on the fourth.
            foreach (var pos in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1) })
            {
                entMan.SpawnEntity("WallSolid", Coords(pos.Item1, pos.Item2));
            }

            target = entMan.SpawnEntity("MobHuman", Coords(0, 0));
            var factions = entMan.System<NpcFactionSystem>();
            factions.ClearFactions(target);
            factions.AddFaction(target, "NanoTrasen");

            obstacle = entMan.SpawnEntity(obstacleProto, Coords(1, 0));
            Assert.That(entMan.GetComponent<TransformComponent>(obstacle).Anchored, Is.False,
                "Test expects the obstacle to be a dynamic (unanchored) entity");

            npc = entMan.SpawnEntity("MobXeno", Coords(6, 0));
            entMan.System<NPCSystem>().WakeNPC(npc);
        });

        var damageable = entMan.GetComponent<DamageableComponent>(target);
        await server.WaitRunTicks(5);
        var baseBrute = Brute(damageable);
        var ticks = MaxSeconds * 30;
        var broken = false;

        for (var i = 0; i < ticks; i += 30)
        {
            await server.WaitRunTicks(30);

            var oDmg = entMan.Deleted(obstacle) ? "deleted" : entMan.GetComponent<DamageableComponent>(obstacle).TotalDamage.ToString();
            var oPos = entMan.Deleted(obstacle) ? "-" : entMan.GetComponent<TransformComponent>(obstacle).Coordinates.Position.ToString();
            TestContext.Out.WriteLine($"t={i / 30}s npc={entMan.GetComponent<TransformComponent>(npc).Coordinates.Position} obstacle={oPos} obstacleDmg={oDmg} targetBrute={Brute(damageable)} targetPos={entMan.GetComponent<TransformComponent>(target).Coordinates.Position}");
            if (entMan.Deleted(obstacle) || Brute(damageable) > baseBrute)
            {
                broken = true;
                break;
            }
        }

        await server.WaitAssertion(() =>
        {
            var npcPos = entMan.GetComponent<TransformComponent>(npc).Coordinates.Position;
            Assert.That(broken, Is.True,
                $"Hostile NPC did not break the {obstacleProto} barricade in {MaxSeconds}s (npc at {npcPos})");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Only melee-style damage counts: the test map has no atmosphere so the target also takes asphyxiation.
    /// </summary>
    private static float Brute(DamageableComponent damageable)
    {
        var total = 0f;

        foreach (var type in new[] { "Blunt", "Slash", "Piercing" })
        {
            if (damageable.Damage.DamageDict.TryGetValue(type, out var value))
                total += value.Float();
        }

        return total;
    }
}
