using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Horizon.GravityGun;
using Content.Shared._Horizon.GravityGun;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Horizon;

/// <summary>
/// The gravity gun grabs a loose item and the physics joint brings it to where the user aims.
/// </summary>
[TestFixture]
public sealed class GravityGunTest
{
    private static readonly PoolSettings Settings = new() { FailureLogLevel = LogLevel.Error, Dirty = true };

    [Test]
    public async Task GrabbedItemFollowsTheAim()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;

        EntityUid user = default, gun = default, item = default;
        MapCoordinates itemPos = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            user = entMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, mapId));
            item = entMan.SpawnEntity("Crowbar", new MapCoordinates(6, 0, mapId));
            gun = entMan.SpawnEntity("WeaponGravityGun", new MapCoordinates(0, 0, mapId));
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, gun), Is.True);
            itemPos = new MapCoordinates(6, 0, mapId);

            entMan.System<SharedInteractionSystem>().UserInteraction(user, entMan.System<SharedTransformSystem>().ToCoordinates(itemPos), item);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<GravityGunComponent>(gun);
            Assert.That(comp.Held, Is.EqualTo(item), "grabbed");
            Assert.That(comp.Tether, Is.Not.Null, "held by a joint");

            // Bring it closer, as the mouse wheel does.
            comp.HoldDistance = 3f;
        });

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            var pos = entMan.System<SharedTransformSystem>().GetWorldPosition(item);
            Assert.That(pos.X, Is.InRange(2f, 4f), $"the item came to the held distance, is at {pos}");
            Assert.That(entMan.GetComponent<GravityGunComponent>(gun).Held, Is.EqualTo(item));
        });

        await pair.CleanReturnAsync();
    }
}
