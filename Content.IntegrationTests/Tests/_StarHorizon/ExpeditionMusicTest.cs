using System.Linq;
using Content.Client._StarHorizon.Expedition;
using Content.Server.Radio.EntitySystems;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Inventory;
using Content.Shared.Radio.Components;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Музыка экспедиции с ключом шифрования: файлы на месте и разложены по группам, сетевые состояния доходят до клиента,
/// ключ в гарнитуре игрока включает музыку, если его вынуть, она выключается.
/// </summary>
[TestFixture]
public sealed class ExpeditionMusicTest
{
    [Test]
    public async Task MusicFilesAreGrouped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var library = ExpeditionMusicLibrary.Load(pair.Client.Resolve<IResourceManager>());

        Assert.That(library.Ambiance, Is.Not.Empty, "фон");
        Assert.That(library.Tittle, Is.Not.Empty, "стингер посадки");
        Assert.That(library.Wave, Is.Not.Empty, "волна");
        Assert.That(library.Extraction, Is.Not.Empty, "эвакуация");
        Assert.That(library.Win, Is.Not.Empty, "победа");
        Assert.That(library.Loss, Is.Not.Empty, "проигрыш");
        Assert.That(library.Rig, Is.Not.Null, "ожидание в капсуле");
        Assert.That(library.Intro, Is.Not.Null, "интро волны");
        Assert.That(library.Loop, Is.Not.Null, "петля волны");
        Assert.That(library.Wave.Any(p => p.Contains("_WD-")), Is.False, "интро, петля и босс не попадают в обычные треки волны");

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
        var container = server.System<SharedContainerSystem>();
        var music = client.System<ExpeditionMusicSystem>();

        EntityUid mob = default, headset = default, key = default, mapEntity = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            headset = server.EntMan.SpawnEntity("ClothingHeadsetGrey", map.GridCoords);
            key = server.EntMan.SpawnEntity("StarHorizonExpeditionMusicKey", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, headset, "ears", force: true));
            Assert.That(container.TryGetContainer(headset, EncryptionKeyHolderComponent.KeyContainerName, out var keys));
            Assert.That(container.Insert(key, keys!), "ключ вставлен в гарнитуру");
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

        // Файлы стыка интро -> петля подгружены заранее, по одному за проверку, а не в момент перехода.
        var library = ExpeditionMusicLibrary.Load(client.Resolve<IResourceManager>());
        await pair.RunSeconds(10f);
        Assert.That(music.IsPreloaded(library.Intro!), "интро загружено заранее");
        Assert.That(music.IsPreloaded(library.Loop!), "петля загружена заранее");
        var clientMap = client.EntMan.GetEntity(server.EntMan.GetNetEntity(mapEntity));
        Assert.That(client.EntMan.GetComponent<ExpeditionWaveStateComponent>(clientMap).Active, "клиент знает про волну");
        Assert.That(client.EntMan.GetComponent<ExpeditionExtractionStateComponent>(clientMap).Active, "клиент знает про эвакуацию");

        // Ключ вынули: музыка выключена.
        await server.WaitPost(() => container.Remove(key, container.GetContainer(headset, EncryptionKeyHolderComponent.KeyContainerName)));
        await pair.RunSeconds(1.5f);
        Assert.That(music.LocalHasKey, Is.False, "ключ вынут: музыка выключена");

        await pair.CleanReturnAsync();
    }
}
