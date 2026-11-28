using System.IO;
using System.Linq;
using Content.Server._StarHorizon.DropPod;
using Content.Server.NPC.HTN;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.NPC;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Синдикатская капсула с грузом (волны, приёмник): метка посадки, падение с анимацией, через 3 с открывается и выпускает груз; ИИ мобов
/// внутри выключен и включается при выходе; пустая капсула потом исчезает.
/// </summary>
[TestFixture]
public sealed class ExpeditionCargoPodTest
{
    [Test]
    public async Task CargoPodFallsOpensAndReleases()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var container = server.System<SharedContainerSystem>();
        var appearance = server.System<SharedAppearanceSystem>();

        EntityUid pod = default, mob = default;
        await server.WaitPost(() =>
        {
            mob = server.EntMan.SpawnEntity("NFMobXeno", map.GridCoords);
            pod = server.System<ExpeditionCargoPodSystem>().Drop(map.GridCoords, new[] { mob });
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(pod).EntityPrototype?.ID, Is.EqualTo("ExpeditionCargoPod"));
        });

        ExpeditionCargoPodStage Stage() =>
            appearance.TryGetData<ExpeditionCargoPodStage>(pod, ExpeditionCargoPodVisuals.Stage, out var stage) ? stage : default;

        int Count(string proto) => server.EntMan.AllEntities<MetaDataComponent>().Count(e => e.Comp.EntityPrototype?.ID == proto);

        // Сначала горит метка места посадки; капсулы и падения ещё нет; моб внутри, ИИ выключен.
        await pair.RunTicksSync(2);
        Assert.That(Stage(), Is.EqualTo(ExpeditionCargoPodStage.Falling));
        Assert.That(Count(ExpeditionCargoPodSystem.MarkerProto), Is.EqualTo(1), "метка места посадки");
        Assert.That(Count(ExpeditionCargoPodSystem.LandingAnimation), Is.EqualTo(0), "падение ещё не началось");
        Assert.That(container.IsEntityInContainer(mob), "моб в капсуле");
        Assert.That(server.EntMan.GetComponent<HTNComponent>(mob).Enabled, Is.False, "ИИ в капсуле выключен");
        Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(mob), Is.False);

        // Через 5 с: падение (анимация).
        await pair.RunSeconds(5.2f);
        Assert.That(Count(ExpeditionCargoPodSystem.LandingAnimation), Is.EqualTo(1), "анимация падения");

        // Приземлилась (1.81 с): стоит закрытая, метки больше нет.
        await pair.RunSeconds(2f);
        Assert.That(Stage(), Is.EqualTo(ExpeditionCargoPodStage.Landed));
        Assert.That(Count(ExpeditionCargoPodSystem.MarkerProto), Is.EqualTo(0), "метка исчезла");
        Assert.That(container.IsEntityInContainer(mob), "ещё закрыта");

        // Через 3 с открылась: моб вышел, ИИ включён.
        await pair.RunSeconds(1.5f);
        Assert.That(container.IsEntityInContainer(mob), "открывается не сразу");
        await pair.RunSeconds(2f);
        Assert.That(Stage(), Is.EqualTo(ExpeditionCargoPodStage.Open));
        Assert.That(container.IsEntityInContainer(mob), Is.False, "моб вышел");
        Assert.That(server.EntMan.GetComponent<HTNComponent>(mob).Enabled, "ИИ включён");
        Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(mob));

        // Пустая капсула исчезает.
        await server.WaitPost(() => server.EntMan.GetComponent<ExpeditionCargoPodComponent>(pod).DeleteAt = TimeSpan.Zero);
        await pair.RunTicksSync(3);
        Assert.That(server.EntMan.Deleted(pod));
        Assert.That(server.EntMan.Deleted(mob), Is.False, "моб остался");

        await pair.CleanReturnAsync();
    }

    /// <summary>Наши прототипы дроп-подов и экспедиций не берут спрайты Goobstation (там спрайты tgstation).</summary>
    [Test]
    public void NoGoobstationSpritesInStarHorizonPrototypes()
    {
        // Корень репозитория: вверх от папки сборки тестов до каталога с Resources/Prototypes.
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Resources", "Prototypes")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "корень репозитория найден");
        var root = Path.Combine(dir!.FullName, "Resources", "Prototypes", "_StarHorizon");

        var offenders = Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllLines(f).Any(l => l.Contains("_Goobstation/") && !l.TrimStart().StartsWith('#')))
            .ToList();
        Assert.That(offenders, Is.Empty);
    }
}
