using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Horizon.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Melee;
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

        // There are no players on the test server, so NPCs would be put to sleep by the distance pause.
        await server.WaitPost(() => server.CfgMan.SetCVar(HorizonCCVars.NpcPauseWhenNoPlayersInRange, false));

        var map = await pair.CreateTestMap();

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

            // Target is boxed in by walls on three sides and the obstacle on the fourth:
            //   W W W
            //   W T O . . . . X
            //   W W W
            foreach (var (x, y) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1) })
            {
                entMan.SpawnEntity("WallSolid", Coords(x, y));
            }

            var target = entMan.SpawnEntity("MobHuman", Coords(0, 0));
            var factions = entMan.System<NpcFactionSystem>();
            factions.ClearFactions(target);
            factions.AddFaction(target, "NanoTrasen");
            // The test map is a vacuum: keep the target alive.
            entMan.RemoveComponent<BarotraumaComponent>(target);
            entMan.RemoveComponent<RespiratorComponent>(target);

            obstacle = entMan.SpawnEntity(obstacleProto, Coords(1, 0));
            Assert.That(entMan.GetComponent<TransformComponent>(obstacle).Anchored, Is.False,
                "Test expects the obstacle to be a dynamic (unanchored) entity");

            npc = entMan.SpawnEntity("MobXeno", Coords(6, 0));
            // This test is about the AI, not balance: make sure each hit actually gets through the
            // locker's flat damage reduction (StructuralMetallic eats 10 of each brute type per hit).
            entMan.GetComponent<MeleeWeaponComponent>(npc).Damage =
                new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 40);
            entMan.System<NPCSystem>().WakeNPC(npc);
        });

        var broken = false;

        for (var i = 0; i < MaxSeconds && !broken; i++)
        {
            await server.WaitRunTicks(30);
            broken = entMan.Deleted(obstacle);
        }

        await server.WaitAssertion(() =>
        {
            var npcPos = entMan.GetComponent<TransformComponent>(npc).Coordinates.Position;
            Assert.That(broken, Is.True,
                $"Hostile NPC did not break the {obstacleProto} barricade in {MaxSeconds}s (npc at {npcPos})");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(HorizonCCVars.NpcPauseWhenNoPlayersInRange, true));
        await pair.CleanReturnAsync();
    }
}
