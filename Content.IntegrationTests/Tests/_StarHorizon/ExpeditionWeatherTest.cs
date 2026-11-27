using System.Collections.Generic;
using System.Linq;
using Content.Server.Salvage;
using Content.Shared.Salvage.Expeditions.Modifiers;
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
                foreach (var biome in new[] { "Snow", "Grasslands", "Lava", "Caves" })
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
}
