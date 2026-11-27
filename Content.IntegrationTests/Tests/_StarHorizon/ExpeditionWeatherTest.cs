using System.Collections.Generic;
using System.Linq;
using Content.Server.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Content.Shared._Horizon.SpaceHazards;
using Robust.Shared.GameObjects;
using Content.Shared.Weather;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Погода экспедиций (порт модификаторов с Monolith): у задания на планете с атмосферой есть погода, подходящая
/// биому, и разная погода действительно выпадает.
/// </summary>
[TestFixture]
public sealed class ExpeditionWeatherTest
{
    [Test]
    public async Task WeatherMatchesTheBiome()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var salvage = server.System<SalvageSystem>();
        var proto = server.ProtoMan;

        // Все погодные модификаторы ссылаются на существующую погоду.
        foreach (var mod in proto.EnumeratePrototypes<SalvageWeatherMod>())
            Assert.That(proto.HasIndex<WeatherPrototype>(mod.WeatherPrototype), $"{mod.ID}: погода {mod.WeatherPrototype}");

        var seen = new Dictionary<string, HashSet<string>>();
        await server.WaitPost(() =>
        {
            foreach (var difficulty in new[] { "NFModerate", "NFHazardous", "NFExtreme" })
            {
                var diff = proto.Index<Content.Shared.Procedural.SalvageDifficultyPrototype>(difficulty);
                foreach (var biome in new[] { "Snow", "Grasslands", "Lava", "Caves", "Desert", "Crocus", "Ocean" })
                {
                    for (var seed = 1; seed <= 150; seed++)
                    {
                        var mission = salvage.GetMission(Content.Shared.Salvage.SalvageMissionType.Destruction, diff, seed, biome);
                        var air = proto.Index<SalvageAirMod>(mission.Air);
                        if (air.Space)
                        {
                            Assert.That(mission.Weather, Is.Null, "в вакууме погоды нет");
                            continue;
                        }

                        Assert.That(mission.Weather, Is.Not.Null, $"{biome} #{seed}: есть погода");
                        var weather = proto.Index<SalvageWeatherMod>(mission.Weather!);
                        Assert.That(weather.Biomes == null || weather.Biomes.Contains(biome), $"{weather.ID} подходит биому {biome}");
                        seen.GetOrNew(biome).Add(weather.ID);
                    }
                }
            }
        });

        Assert.That(seen["Snow"], Does.Contain("SnowfallHeavy").Or.Contain("Hail").Or.Contain("SnowfallMedium"), "на снегу выпадает снегопад или град");
        Assert.That(seen["Lava"].Any(w => w.StartsWith("Ashfall")), "на лаве выпадает пеплопад");
        Assert.That(seen["Grasslands"].Count, Is.GreaterThan(2), "погода разная");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Биомы Monolith (пустыня, лес Крокус, океан): задание собирается на любой сложности (свет, температура, воздух,
    /// данж и погода находятся), а планета экспедиции генерируется с данжем.
    /// </summary>
    [TestCase("Desert")]
    [TestCase("Crocus")]
    [TestCase("Ocean")]
    public async Task MonolithBiomesGenerate(string biome)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Dirty = true });
        var server = pair.Server;
        var salvage = server.System<SalvageSystem>();
        var proto = server.ProtoMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            foreach (var difficulty in proto.EnumeratePrototypes<Content.Shared.Procedural.SalvageDifficultyPrototype>())
            {
                for (var seed = 1; seed <= 60; seed++)
                {
                    var mission = salvage.GetMission(Content.Shared.Salvage.SalvageMissionType.Destruction, difficulty, seed, biome);
                    Assert.That(mission.Biome, Is.EqualTo(biome));
                    var dungeon = proto.Index<SalvageDungeonModPrototype>(mission.Dungeon);
                    Assert.That(dungeon.Biomes == null || dungeon.Biomes.Contains(biome), $"{dungeon.ID} подходит {biome}");
                }
            }
        });

        EntityUid planet = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new Robust.Shared.Map.MapCoordinates(new System.Numerics.Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<Content.Shared._Horizon.SpaceHazards.ExpeditionPlanetComponent>(planet);
            server.System<Content.Server._Horizon.SpaceHazards.ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, biome, 0.5f);
            salvage.StartPlanetExpedition((planet, planetComp));
        });

        var comp = server.EntMan.GetComponent<Content.Shared._Horizon.SpaceHazards.ExpeditionPlanetComponent>(planet);
        for (var attempt = 0; attempt < 3 && comp.Status != PlanetExpeditionStatus.Active; attempt++)
        {
            if (comp.Status == PlanetExpeditionStatus.Idle && attempt > 0)
                await server.WaitPost(() => salvage.StartPlanetExpedition((planet, comp)));

            for (var i = 0; i < 60 && comp.Status == PlanetExpeditionStatus.Generating; i++)
                await pair.RunSeconds(2f);
        }

        Assert.That(comp.Status, Is.EqualTo(PlanetExpeditionStatus.Active), $"планета {biome} сгенерирована");
        var expeditionMap = comp.ExpeditionMap!.Value;
        Assert.That(server.EntMan.GetComponent<Content.Shared.Parallax.Biomes.BiomeComponent>(expeditionMap).Template, Is.Not.Null);

        await pair.CleanReturnAsync();
    }
}
