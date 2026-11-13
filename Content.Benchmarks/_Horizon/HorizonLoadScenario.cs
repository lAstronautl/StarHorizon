using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Content.IntegrationTests;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Server.Shuttles.Components;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Benchmarks._Horizon;

/// <summary>
/// Нагрузочный сценарий сервера StarHorizon: настоящий раунд Frontier (аванпост + точки интереса),
/// корабли, фиктивные игроки, NPC и возмущения атмосферы. Меряет время тика сервера,
/// разбивку по EntitySystem, аллокации и GC. Используется для сравнения «до/после».
/// Запуск: Content.Benchmarks horizon-load --label base --out dir [опции].
/// </summary>
public static class HorizonLoadScenario
{
    private sealed class Options
    {
        public string Label = "run";
        public string OutDir = ".";
        public string Preset = "nfadventure";
        public int Players = 40;
        public int Ships = 30;
        public int Npcs = 60;
        public int WarmupTicks = 600;
        public int MeasureTicks = 2000;
        public int Seed = 42;
        public int ParallelCount = 0;
        public bool Pvs = true;
        public bool PrintExceptions;
        /// <summary>Период «оборота» мира в тиках (0 — выключен): смена кораблей, игроков, предметов, разведка космоса.</summary>
        public int Churn;
        /// <summary>Период контрольных точек памяти в тиках (0 — выключены). Каждая точка делает полную сборку мусора.</summary>
        public int MemEvery;
        /// <summary>Сколько NPC поставить на корабли без игроков (для замера сна NPC вдали от игроков).</summary>
        public int FarNpcs;
        public string PathOffset = "";
        public readonly List<(string Name, string Value)> CVars = new();
    }

    private static int _exceptions;
    private static volatile bool _countExceptions;

    private static readonly string[] NpcProtos = { "MobMouse", "MobCarp", "MobCorgi", "MobXeno" };

    public static int Run(string[] args)
    {
        var opts = Parse(args);
        try
        {
            RunAsync(opts).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[horizon-load] FAILED: {e}");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => args[++i];
            switch (args[i])
            {
                case "--label": o.Label = Next(); break;
                case "--out": o.OutDir = Next(); break;
                case "--preset": o.Preset = Next(); break;
                case "--players": o.Players = int.Parse(Next()); break;
                case "--ships": o.Ships = int.Parse(Next()); break;
                case "--npcs": o.Npcs = int.Parse(Next()); break;
                case "--warmup": o.WarmupTicks = int.Parse(Next()); break;
                case "--ticks": o.MeasureTicks = int.Parse(Next()); break;
                case "--seed": o.Seed = int.Parse(Next()); break;
                case "--parallel": o.ParallelCount = int.Parse(Next()); break;
                case "--pvs": o.Pvs = bool.Parse(Next()); break;
                case "--print-exceptions": o.PrintExceptions = true; break;
                case "--churn": o.Churn = int.Parse(Next()); break;
                case "--mem-every": o.MemEvery = int.Parse(Next()); break;
                case "--far-npcs": o.FarNpcs = int.Parse(Next()); break;
                case "--path-offset": o.PathOffset = Next(); break;
                case "--cvar":
                    var kv = Next().Split('=', 2);
                    o.CVars.Add((kv[0], kv[1]));
                    break;
            }
        }

        return o;
    }

    private static void Log(string msg)
    {
        Console.WriteLine($"[horizon-load {DateTime.UtcNow:HH:mm:ss}] {msg}");
    }

    /// <summary>Память по этапам запуска: живые объекты после полной сборки мусора и память процесса.</summary>
    private static void LogStageMemory(string stage)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Log($"STAGE {stage}: heapLive={GC.GetTotalMemory(false) / 1048576}MB rss={Process.GetCurrentProcess().WorkingSet64 / 1048576}MB");
    }

    private static async Task RunAsync(Options o)
    {
        Directory.CreateDirectory(o.OutDir);

        // Лог-обработчик тестового пула не печатает исключения, поэтому считаем и (по запросу) печатаем их сами.
        AppDomain.CurrentDomain.FirstChanceException += (_, ev) =>
        {
            if (!_countExceptions)
                return;

            _exceptions++;
            if (o.PrintExceptions && _exceptions <= 40)
                Console.WriteLine($"[horizon-load] first-chance exception: {ev.Exception}");
        };
        var total = Stopwatch.StartNew();

        ProgramShared.PathOffset = o.PathOffset;
        PoolManager.Startup();

        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            InLobby = true,
            Dirty = true,
            Fresh = true,
            // Тестовый пул по умолчанию валит прогон на любом предупреждении в логе; здесь это не нужно.
            FailureLogLevel = Robust.Shared.Log.LogLevel.Fatal,
        }, new ExternalTestContext("horizon-load", Console.Out));
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        Log($"server up in {total.Elapsed.TotalSeconds:F1}s");
        LogStageMemory("server+client started (prototypes, resources)");

        // Приближаем настройки к боевым: тестовый пул отключает PVS, параллелизм и генерацию мира.
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CVars.NetPVS, o.Pvs);
            cfg.SetCVar(CVars.ThreadParallelCount, o.ParallelCount);
            cfg.SetCVar(CCVars.NPCMaxUpdates, 128);
            cfg.SetCVar(CCVars.WorldgenEnabled, true);
            cfg.SetCVar(CCVars.GameMap, "Frontier");
            cfg.SetCVar(CCVars.GameLobbyDefaultPreset, o.Preset);
            foreach (var (name, value) in o.CVars)
            {
                cfg.SetCVar(name, value);
            }

            // Тестовый пул задаёт серверному генератору зерно, а Random(seed) — старая реализация .NET.
            // На боевом сервере зерна нет, поэтому возвращаем обычный генератор.
            var robustRandom = server.ResolveDependency<IRobustRandom>();
            robustRandom.GetType()
                .GetField("_random", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(robustRandom, new Random());
            // Зерно серверному IRobustRandom не задаём: Random(seed) — старая реализация .NET, которая при
            // обращении из нескольких потоков (параллельные задачи игры) ломается и выдаёт одни нули.
        });

        _countExceptions = true;
        var ticker = server.System<GameTicker>();
        var sw = Stopwatch.StartNew();
        await server.WaitPost(() =>
        {
            // Пресет выбирается при входе в лобби, поэтому одного CVar-а недостаточно.
            ticker.SetGamePreset(o.Preset);
            ticker.StartRound();
        });
        await server.WaitRunTicks(30);
        Log($"round started ({o.Preset}) in {sw.Elapsed.TotalSeconds:F1}s, runlevel={ticker.RunLevel}");
        LogStageMemory($"round started, entities={entMan.EntityCount}");

        var mapId = ticker.DefaultMap;
        var mapLoader = entMan.System<MapLoaderSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var rng = new Random(o.Seed);

        // --- Корабли: детерминированная выборка верфи, по кольцам вокруг аванпоста.
        sw.Restart();
        var ships = new List<EntityUid>();
        var vessels = protoMan.EnumeratePrototypes<VesselPrototype>()
            .Where(v => v.ShuttlePath != default)
            .OrderBy(v => v.ID, StringComparer.Ordinal)
            .ToList();
        var step = Math.Max(1, vessels.Count / Math.Max(1, o.Ships));
        await server.WaitPost(() =>
        {
            for (var i = 0; i < o.Ships && i * step < vessels.Count; i++)
            {
                var vessel = vessels[i * step];
                var angle = i * 2.399963f; // золотой угол
                var radius = 260f + 45f * i;
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                try
                {
                    if (mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid, offset: offset))
                        ships.Add(grid.Value.Owner);
                }
                catch (Exception e)
                {
                    Log($"ship {vessel.ID} failed to load: {e.Message}");
                }
            }
        });
        await server.WaitRunTicks(5);
        Log($"loaded {ships.Count} ships in {sw.Elapsed.TotalSeconds:F1}s");
        LogStageMemory($"ships loaded, entities={entMan.EntityCount}");

        // --- Точки появления игроков: пульты шаттлов на кораблях и на аванпосте.
        var shipSet = ships.ToHashSet();
        var shipSpots = new List<EntityCoordinates>();
        var stationSpots = new List<EntityCoordinates>();
        await server.WaitPost(() =>
        {
            var perShip = new Dictionary<EntityUid, EntityCoordinates>();
            var query = entMan.AllEntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            var all = new List<(EntityUid Uid, TransformComponent Xform)>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                all.Add((uid, xform));
            }

            foreach (var (uid, xform) in all.OrderBy(x => x.Uid.Id))
            {
                if (xform.MapID != mapId || xform.GridUid is not { } grid)
                    continue;

                if (shipSet.Contains(grid))
                    perShip.TryAdd(grid, xform.Coordinates);
                else
                    stationSpots.Add(xform.Coordinates);
            }

            foreach (var ship in ships)
            {
                if (perShip.TryGetValue(ship, out var coords))
                    shipSpots.Add(coords);
            }
        });

        if (stationSpots.Count == 0)
            throw new Exception("No station spawn spots found on the default map");
        if (shipSpots.Count == 0)
            shipSpots.AddRange(stationSpots);

        // --- Игроки.
        sw.Restart();
        var sessions = await server.AddDummySessions(o.Players);
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var mobs = new EntityUid[o.Players];
        await server.WaitPost(() =>
        {
            var mind = entMan.System<MindSystem>();
            var god = entMan.System<SharedGodmodeSystem>();
            for (var i = 0; i < o.Players; i++)
            {
                var onShip = i % 2 == 1;
                var spot = onShip ? shipSpots[(i / 2) % shipSpots.Count] : stationSpots[(i / 2) % stationSpots.Count];
                var uid = entMan.SpawnEntity("MobHuman", spot);
                server.ConsoleHost.ExecuteCommand($"setoutfit {entMan.GetNetEntity(uid)} PassengerGear");
                mind.ControlMob(sessions[i].UserId, uid);
                god.EnableGodmode(uid);
                playerMan.JoinGame(sessions[i]);
                mobs[i] = uid;
            }
        });
        Log($"spawned {o.Players} players in {sw.Elapsed.TotalSeconds:F1}s");

        // --- NPC рядом с игроками (игроки бессмертны, поэтому бой не затухает).
        await server.WaitPost(() =>
        {
            for (var i = 0; i < o.Npcs; i++)
            {
                var anchor = mobs[i % mobs.Length];
                var coords = entMan.GetComponent<TransformComponent>(anchor).Coordinates;
                try
                {
                    entMan.SpawnEntity(NpcProtos[i % NpcProtos.Length], coords);
                }
                catch (Exception e)
                {
                    Log($"npc spawn failed: {e.Message}");
                }
            }
        });

        // --- NPC вдали от игроков: на кораблях, где игроков нет.
        if (o.FarNpcs > 0)
        {
            var emptyShipSpots = shipSpots.Skip(Math.Min(shipSpots.Count, (o.Players + 1) / 2)).ToList();
            if (emptyShipSpots.Count == 0)
                Log("no player-less ships for --far-npcs");

            await server.WaitPost(() =>
            {
                for (var i = 0; i < o.FarNpcs && emptyShipSpots.Count > 0; i++)
                {
                    try
                    {
                        entMan.SpawnEntity(NpcProtos[i % NpcProtos.Length], emptyShipSpots[i % emptyShipSpots.Count]);
                    }
                    catch (Exception ex)
                    {
                        Log($"far npc spawn failed: {ex.Message}");
                    }
                }
            });
        }

        var mover = entMan.System<SharedMoverController>();
        var atmos = entMan.System<AtmosphereSystem>();
        var directions = new[] { Direction.North, Direction.East, Direction.South, Direction.West };
        var held = new Direction?[o.Players];

        // Детерминированные действия «игроков» на каждом тике сценария.
        void Act(int tick)
        {
            // Игроки меняют направление движения раз в 45 тиков (со сдвигом по игрокам).
            for (var i = 0; i < mobs.Length; i++)
            {
                if ((tick + i * 7) % 45 != 0)
                    continue;

                if (!entMan.TryGetComponent(mobs[i], out InputMoverComponent? input))
                    continue;

                if (held[i] is { } old)
                    mover.SetVelocityDirection((mobs[i], input), old, 0, false);

                var dir = directions[rng.Next(directions.Length)];
                mover.SetVelocityDirection((mobs[i], input), dir, 0, true);
                held[i] = dir;
            }

            // Треть кораблей летит: раз в 300 тиков получают импульс.
            if (tick % 300 == 0)
            {
                for (var i = 0; i < ships.Count; i += 3)
                {
                    if (!entMan.TryGetComponent(ships[i], out PhysicsComponent? body))
                        continue;

                    var a = (float)(rng.NextDouble() * Math.PI * 2);
                    physics.SetLinearVelocity(ships[i], new Vector2(MathF.Cos(a), MathF.Sin(a)) * 12f, body: body);
                    physics.SetAngularVelocity(ships[i], 0.05f, body: body);
                }
            }

            // Возмущение атмосферы: раз в 200 тиков впрыск азота в клетку очередного игрока.
            if (tick % 200 == 0)
            {
                var uid = mobs[(tick / 200) % mobs.Length];
                var mix = atmos.GetTileMixture(uid, excite: true);
                mix?.AdjustMoles(Gas.Nitrogen, 400f);
            }
        }

        // --- «Оборот» мира: ускоренная имитация долгой смены (для поиска роста памяти).
        var mapSys = entMan.System<SharedMapSystem>();
        var churnItems = new List<EntityUid>();
        var playerShips = Math.Min(ships.Count, (o.Players + 1) / 2);

        async Task Churn(int tick)
        {
            if (o.Churn <= 0 || tick == 0 || tick % o.Churn != 0)
                return;

            var c = tick / o.Churn;

            // 1) Отключение и подключение игрока (нечётные индексы — игроки на кораблях).
            var s = 1 + 2 * (c % Math.Max(1, o.Players / 2));
            if (s < sessions.Length)
            {
                EntityCoordinates spot = default;
                await server.WaitPost(() =>
                {
                    spot = entMan.TryGetComponent(mobs[s], out TransformComponent? oldXform)
                        ? oldXform.Coordinates
                        : stationSpots[s % stationSpots.Count];
                    entMan.QueueDeleteEntity(mobs[s]);
                });
                await server.RemoveDummySession(sessions[s], true);
                sessions[s] = await server.AddDummySession($"churn_{c}");
                await server.WaitPost(() =>
                {
                    var uid = entMan.SpawnEntity("MobHuman", spot);
                    server.ConsoleHost.ExecuteCommand($"setoutfit {entMan.GetNetEntity(uid)} PassengerGear");
                    entMan.System<MindSystem>().ControlMob(sessions[s].UserId, uid);
                    entMan.System<SharedGodmodeSystem>().EnableGodmode(uid);
                    playerMan.JoinGame(sessions[s]);
                    mobs[s] = uid;
                    held[s] = null;
                });
            }

            await server.WaitPost(() =>
            {
                // 2) Корабль без игроков удаляется, на его место прилетает другой.
                if (ships.Count > playerShips + 1)
                {
                    var j = playerShips + c % (ships.Count - playerShips);
                    entMan.QueueDeleteEntity(ships[j]);
                    var vessel = vessels[(c * 7) % vessels.Count];
                    var angle = j * 2.399963f;
                    var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (260f + 45f * j);
                    try
                    {
                        if (mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid, offset: offset))
                            ships[j] = grid.Value.Owner;
                    }
                    catch (Exception ex)
                    {
                        Log($"churn ship {vessel.ID} failed to load: {ex.Message}");
                    }
                }

                // 3) Игрок с аванпоста улетает в новый участок космоса (генерация мира вокруг него).
                var e = 2 * (c % Math.Max(1, (o.Players + 1) / 2));
                if (e < mobs.Length && mapSys.TryGetMap(mapId, out var mapUid))
                {
                    var a = c * 2.399963f;
                    var pos = new Vector2(MathF.Cos(a), MathF.Sin(a)) * (3000f + (c % 25) * 400f);
                    xformSys.SetCoordinates(mobs[e], new EntityCoordinates(mapUid.Value, pos));
                }

                // 4) Предметы: прошлая партия удаляется, появляется новая.
                foreach (var item in churnItems)
                {
                    entMan.QueueDeleteEntity(item);
                }
                churnItems.Clear();
                if (!entMan.TryGetComponent(mobs[c % mobs.Length], out TransformComponent? atXform))
                    return;
                var at = atXform.Coordinates;
                for (var i = 0; i < 30; i++)
                {
                    churnItems.Add(entMan.SpawnEntity("Paper", at));
                }
            });
        }

        // --- Контрольные точки памяти (после полной сборки мусора, чтобы видеть живые объекты, а не мусор).
        var memLines = new List<string> { "tick,entities,grids,rssMB,heapLiveMB,gcCommittedMB,gcFragmentedMB" };

        async Task MemCheckpoint(int tick)
        {
            if (o.MemEvery <= 0 || tick % o.MemEvery != 0)
                return;

            int ents = 0, grids = 0;
            await server.WaitPost(() =>
            {
                ents = entMan.EntityCount;
                grids = entMan.Count<MapGridComponent>();
            });
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var info = GC.GetGCMemoryInfo();
            const double mb = 1024 * 1024;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{tick},{ents},{grids},{Process.GetCurrentProcess().WorkingSet64 / mb:F0},{GC.GetTotalMemory(false) / mb:F0},{info.TotalCommittedBytes / mb:F0},{info.FragmentedBytes / mb:F0}");
            memLines.Add(line);
            Log($"MEM {line}");
        }

        // --- Прогрев (JIT, tiered compilation, прибытие FTL, успокоение физики).
        sw.Restart();
        for (var t = 0; t < o.WarmupTicks; t++)
        {
            var tick = t;
            await server.WaitPost(() => Act(tick));
            await server.WaitRunTicks(1);
        }
        Log($"warmup {o.WarmupTicks} ticks in {sw.Elapsed.TotalSeconds:F1}s");
        LogStageMemory($"after warmup, entities={entMan.EntityCount}");

        // --- Замер.
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        sysMan.MetricsEnabled = true;
        await server.WaitRunTicks(1);

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.Default;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sysBefore = await ReadSystemMetrics();
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var allocBefore = GC.GetTotalAllocatedBytes(true);
        var pauseBefore = GC.GetTotalPauseDuration();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;

        var tickMs = new double[o.MeasureTicks];
        var wall = Stopwatch.StartNew();
        var tickSw = new Stopwatch();
        for (var t = 0; t < o.MeasureTicks; t++)
        {
            var tick = o.WarmupTicks + t;
            await MemCheckpoint(t);
            await Churn(t);
            await server.WaitPost(() => Act(tick));
            tickSw.Restart();
            await server.WaitRunTicks(1);
            tickMs[t] = tickSw.Elapsed.TotalMilliseconds;
        }
        wall.Stop();
        await MemCheckpoint(o.MeasureTicks);

        var memInfo = GC.GetGCMemoryInfo();
        var rssMb = Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
        var peakRssMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0;
        var heapMb = GC.GetTotalMemory(false) / 1048576.0;
        var committedMb = memInfo.TotalCommittedBytes / 1048576.0;

        var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
        var allocAfter = GC.GetTotalAllocatedBytes(true);
        var pauseAfter = GC.GetTotalPauseDuration();
        var sysAfter = await ReadSystemMetrics();
        sysMan.MetricsEnabled = false;

        var entityCount = 0;
        var gridCount = 0;
        await server.WaitPost(() =>
        {
            entityCount = entMan.EntityCount;
            gridCount = entMan.Count<MapGridComponent>();
        });

        // --- Отчёт.
        var sorted = (double[])tickMs.Clone();
        Array.Sort(sorted);
        double Pct(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

        var mean = tickMs.Average();
        var std = Math.Sqrt(tickMs.Select(x => (x - mean) * (x - mean)).Sum() / tickMs.Length);
        var allocPerTick = (allocAfter - allocBefore) / (double)o.MeasureTicks;
        var cpuMsPerTick = (cpuAfter - cpuBefore).TotalMilliseconds / o.MeasureTicks;
        var pauseMs = (pauseAfter - pauseBefore).TotalMilliseconds;

        var systems = sysAfter
            .Select(kv => (Name: kv.Key, Sec: kv.Value - sysBefore.GetValueOrDefault(kv.Key)))
            .Where(x => x.Sec > 0)
            .OrderByDescending(x => x.Sec)
            .ToList();
        var sysTotalMs = systems.Sum(x => x.Sec) * 1000 / o.MeasureTicks;

        var inv = CultureInfo.InvariantCulture;
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine($"  \"label\": \"{o.Label}\",");
        json.AppendLine($"  \"preset\": \"{o.Preset}\", \"players\": {o.Players}, \"ships\": {ships.Count}, \"npcs\": {o.Npcs},");
        json.AppendLine($"  \"warmupTicks\": {o.WarmupTicks}, \"measureTicks\": {o.MeasureTicks}, \"seed\": {o.Seed},");
        json.AppendLine($"  \"pvs\": {o.Pvs.ToString().ToLowerInvariant()}, \"parallelCount\": {o.ParallelCount},");
        json.AppendLine($"  \"serverGC\": {GCSettings.IsServerGC.ToString().ToLowerInvariant()}, \"gcLatencyMode\": \"{GCSettings.LatencyMode}\", \"processorCount\": {Environment.ProcessorCount},");
        json.AppendLine($"  \"entities\": {entityCount}, \"grids\": {gridCount},");
        json.AppendLine(string.Create(inv, $"  \"tickMs\": {{\"mean\": {mean:F4}, \"std\": {std:F4}, \"p50\": {Pct(0.50):F4}, \"p95\": {Pct(0.95):F4}, \"p99\": {Pct(0.99):F4}, \"max\": {sorted[^1]:F4}}},"));
        json.AppendLine(string.Create(inv, $"  \"systemsMsPerTick\": {sysTotalMs:F4},"));
        json.AppendLine(string.Create(inv, $"  \"cpuMsPerTick\": {cpuMsPerTick:F4},"));
        json.AppendLine(string.Create(inv, $"  \"allocBytesPerTick\": {allocPerTick:F0},"));
        json.AppendLine(string.Create(inv, $"  \"gc\": {{\"gen0\": {GC.CollectionCount(0) - gc0}, \"gen1\": {GC.CollectionCount(1) - gc1}, \"gen2\": {GC.CollectionCount(2) - gc2}, \"pauseMsTotal\": {pauseMs:F2}}},"));
        json.AppendLine(string.Create(inv, $"  \"wallSeconds\": {wall.Elapsed.TotalSeconds:F2},"));
        json.AppendLine(string.Create(inv, $"  \"memory\": {{\"rssMB\": {rssMb:F0}, \"peakRssMB\": {peakRssMb:F0}, \"heapMB\": {heapMb:F0}, \"gcCommittedMB\": {committedMb:F0}, \"churn\": {o.Churn}}},"));
        json.AppendLine("  \"systems\": [");
        for (var i = 0; i < systems.Count; i++)
        {
            var comma = i == systems.Count - 1 ? "" : ",";
            json.AppendLine(string.Create(inv, $"    {{\"name\": \"{systems[i].Name}\", \"msPerTick\": {systems[i].Sec * 1000 / o.MeasureTicks:F5}}}{comma}"));
        }
        json.AppendLine("  ]");
        json.AppendLine("}");

        var jsonPath = Path.Combine(o.OutDir, $"{o.Label}.json");
        await File.WriteAllTextAsync(jsonPath, json.ToString());
        await File.WriteAllLinesAsync(Path.Combine(o.OutDir, $"{o.Label}.ticks.csv"),
            tickMs.Select(x => x.ToString("F4", inv)));

        Log($"RESULT {o.Label}: mean={mean:F3}ms p50={Pct(0.5):F3} p95={Pct(0.95):F3} p99={Pct(0.99):F3} max={sorted[^1]:F1} " +
            $"systems={sysTotalMs:F3}ms cpu={cpuMsPerTick:F3}ms alloc={allocPerTick / 1024:F0}KB/tick " +
            $"gc={GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2} pause={pauseMs:F0}ms " +
            $"ents={entityCount} grids={gridCount} rss={rssMb:F0}MB heap={heapMb:F0}MB committed={committedMb:F0}MB");
        if (memLines.Count > 1)
            await File.WriteAllLinesAsync(Path.Combine(o.OutDir, $"{o.Label}.mem.csv"), memLines);
        foreach (var (name, sec) in systems.Take(25))
        {
            Log(string.Create(inv, $"  {name,-44} {sec * 1000 / o.MeasureTicks,9:F4} ms/tick"));
        }

        Log($"written {jsonPath}; total {total.Elapsed.TotalSeconds:F0}s");

        // Раунд и пул намеренно не разбираем: процесс одноразовый, выходим сразу.
        Console.Out.Flush();
        Environment.Exit(0);
    }

    private static readonly Regex SumLine = new(
        "^robust_entity_systems_update_usage_sum\\{system=\"([^\"]+)\"\\} (\\S+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Суммарное время (сек) по каждой EntitySystem из встроенной метрики движка.
    /// </summary>
    private static async Task<Dictionary<string, double>> ReadSystemMetrics()
    {
        using var stream = new MemoryStream();
        await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        var result = new Dictionary<string, double>();
        foreach (Match m in SumLine.Matches(text))
        {
            result[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return result;
    }
}
