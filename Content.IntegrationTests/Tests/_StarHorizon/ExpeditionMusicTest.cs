using System.Linq;
using Content.Client._StarHorizon.Expedition;
using Content.Server.Radio.EntitySystems;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Radio.Components;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Музыка экспедиции с ключом шифрования: файлы на месте и разложены по группам, сетевые состояния доходят до клиента,
/// ключ в гарнитуре игрока включает музыку, если его вынуть, она выключается.
/// </summary>
[TestFixture]
public sealed class ExpeditionMusicTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: StarHorizonExpeditionMusicKey
  id: ExpeditionMusicTestCustomKey
  components:
  - type: ExpeditionMusicKey
    rig: null
    tittle: null
    ambiance: null
    extraction: null
    win: null
    loss: null
    wave: !type:SoundPathSpecifier
      path: /Audio/_Horizon/PizzaTower/_WD_Pizza_Tower_OST_-_Oregano.ogg
";

    /// <summary>Музыка ключа по его прототипу (данные берутся из прототипа, как у настоящего ключа на клиенте).</summary>
    private static async Task<ExpeditionMusicLibrary> LibraryOf(Robust.UnitTesting.RobustIntegrationTest.ClientIntegrationInstance client, string proto)
    {
        ExpeditionMusicLibrary? library = null;
        await client.WaitAssertion(() =>
        {
            Assert.That(client.ProtoMan.Index<EntityPrototype>(proto).TryGetComponent<ExpeditionMusicKeyComponent>(out var key, client.EntMan.ComponentFactory));
            library = ExpeditionMusicLibrary.FromKey(key!, client.ProtoMan);
        });
        return library!;
    }

    [Test]
    public async Task MusicFilesAreGrouped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var library = await LibraryOf(pair.Client, "StarHorizonExpeditionMusicKey");

        Assert.That(library.Ambiance, Is.Not.Empty, "фон");
        Assert.That(library.Tittle, Is.Not.Empty, "стингер посадки");
        Assert.That(library.Wave, Is.Not.Empty, "волна");
        Assert.That(library.Extraction, Is.Not.Empty, "эвакуация");
        Assert.That(library.Win, Is.Not.Empty, "победа");
        Assert.That(library.Loss, Is.Not.Empty, "проигрыш");
        Assert.That(library.Rig, Is.Not.Empty, "ожидание в капсуле");
        Assert.That(library.Wave.Any(p => p.Contains("_WD-") || p.Contains("_CI_")), Is.False, "в треках волны только _WD_");

        // Свой ключ: музыка задана в его прототипе (один файл), остальные сцены молчат.
        var custom = await LibraryOf(pair.Client, "ExpeditionMusicTestCustomKey");
        Assert.That(custom.Wave, Has.Count.EqualTo(1));
        Assert.That(custom.Wave[0], Does.EndWith("_WD_Pizza_Tower_OST_-_Oregano.ogg"));
        Assert.That(custom.Ambiance, Is.Empty);
        Assert.That(custom.Rig, Is.Empty);

        await pair.CleanReturnAsync();
    }

    [Test]
    public void GaplessStartsJustBeforeTheEnd()
    {
        Assert.That(ExpeditionMusicSystem.ShouldStartNext(12.7f, 5f), Is.False, "середина трека: ждём");
        Assert.That(ExpeditionMusicSystem.ShouldStartNext(12.7f, 12.6f), Is.True, "за долю секунды до конца: стартуем следующий");
        Assert.That(ExpeditionMusicSystem.ShouldStartNext(0f, 0f), Is.False, "неизвестная длина: обычный путь");
    }

    [Test]
    public async Task KeyInHeadsetTogglesMusic()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var inventory = server.System<InventorySystem>();
        var itemSlots = server.System<ItemSlotsSystem>();
        var music = client.System<ExpeditionMusicSystem>();

        EntityUid mob = default, headset = default, key = default, mapEntity = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            headset = server.EntMan.SpawnEntity("ClothingHeadsetGrey", map.GridCoords);
            key = server.EntMan.SpawnEntity("StarHorizonExpeditionMusicKey", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, headset, "ears", force: true));
            Assert.That(itemSlots.TryInsert(headset, ExpeditionMusicKeyComponent.SlotId, key, null), "ключ вставлен в слот гарнитуры");

            // Слот один: второй ключ музыки не лезет, а обычный ключ шифрования в слот музыки не подходит.
            var secondKey = server.EntMan.SpawnEntity("StarHorizonExpeditionMusicKey", map.GridCoords);
            Assert.That(itemSlots.TryInsert(headset, ExpeditionMusicKeyComponent.SlotId, secondKey, null), Is.False, "два ключа музыки не вставить");
            var radioKey = server.EntMan.SpawnEntity("EncryptionKeyCommon", map.GridCoords);
            Assert.That(itemSlots.TryInsert(headset, ExpeditionMusicKeyComponent.SlotId, radioKey, null), Is.False, "обычный ключ в слот музыки не подходит");
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), mob, out _, true);

            // Сетевые состояния попадают на клиент.
            mapEntity = map.MapUid;
            var wave = server.EntMan.AddComponent<ExpeditionWaveStateComponent>(mapEntity);
            wave.Active = true;
            server.EntMan.Dirty(mapEntity, wave);
            var extraction = server.EntMan.AddComponent<ExpeditionExtractionStateComponent>(mapEntity);
            extraction.Active = true;
            server.EntMan.Dirty(mapEntity, extraction);
        });

        await pair.RunSeconds(1.5f);
        Assert.That(music.LocalHasKey, "ключ в гарнитуре: музыка включена");

        // Файлы подгружаются заранее, по одному за проверку, а не в момент смены трека.
        await pair.RunSeconds(10f);
        Assert.That((await LibraryOf(client, "StarHorizonExpeditionMusicKey")).Rig.Any(music.IsPreloaded), "ожидание в капсуле загружено заранее");
        var clientMap = client.EntMan.GetEntity(server.EntMan.GetNetEntity(mapEntity));
        Assert.That(client.EntMan.GetComponent<ExpeditionWaveStateComponent>(clientMap).Active, "клиент знает про волну");
        Assert.That(client.EntMan.GetComponent<ExpeditionExtractionStateComponent>(clientMap).Active, "клиент знает про эвакуацию");

        // Ключ вынули: музыка выключена.
        await server.WaitPost(() => Assert.That(itemSlots.TryEject(headset, ExpeditionMusicKeyComponent.SlotId, null, out _), "ключ вынут из слота"));
        await pair.RunSeconds(1.5f);
        Assert.That(music.LocalHasKey, Is.False, "ключ вынут: музыка выключена");

        await pair.CleanReturnAsync();
    }

    /// <summary>Музыка полёта в капсуле играет только в БСС, не на взлёте; на посадке (экран переключён) её сменяют стингер и фон.</summary>
    [Test]
    public async Task PodMusicPlaysOnlyInBluespace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var inventory = server.System<InventorySystem>();
        var itemSlots = server.System<ItemSlotsSystem>();
        var storage = server.System<Content.Server.Storage.EntitySystems.EntityStorageSystem>();
        var appearance = server.System<Robust.Server.GameObjects.AppearanceSystem>();
        var music = client.System<ExpeditionMusicSystem>();

        EntityUid mob = default, pod = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var headset = server.EntMan.SpawnEntity("ClothingHeadsetGrey", map.GridCoords);
            var key = server.EntMan.SpawnEntity("StarHorizonExpeditionMusicKey", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, headset, "ears", force: true));
            Assert.That(itemSlots.TryInsert(headset, ExpeditionMusicKeyComponent.SlotId, key, null));
            pod = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords);
            storage.OpenStorage(pod);
            storage.Insert(mob, pod);
            storage.CloseStorage(pod);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), mob, out _, true);
        });

        async Task SetState(Content.Shared._StarHorizon.DropPod.DropPodState state)
        {
            await server.WaitPost(() => appearance.SetData(pod, Content.Shared._StarHorizon.DropPod.DropPodVisuals.State, state));
            await pair.RunSeconds(1.5f);
        }

        await SetState(Content.Shared._StarHorizon.DropPod.DropPodState.ZoomingOut);
        Assert.That(music.CurrentScene, Is.EqualTo("None"), "на взлёте музыки полёта нет");

        await SetState(Content.Shared._StarHorizon.DropPod.DropPodState.InBluespace);
        Assert.That(music.CurrentScene, Is.EqualTo("Pod"), "в БСС играет музыка полёта");

        await pair.CleanReturnAsync();
    }
}
