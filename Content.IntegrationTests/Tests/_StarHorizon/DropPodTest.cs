using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._StarHorizon;

[TestFixture]
public sealed class DropPodTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: DropPodTestPassenger
  components:
  - type: Eye
  - type: ContentEye
  - type: MobState
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 50
        mask:
        - MobMask
        layer:
        - MobLayer
";

    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(1);

    private static void MakeFast(DropPodComponent pod)
    {
        pod.CountdownTime = Fast;
        pod.ZoomOutTime = Fast;
        pod.FlightTime = Fast;
        pod.LandingTime = Fast;
    }

    [Test]
    public async Task LaunchToCoordinates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Error });
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, 1f);
        var map = await pair.CreateTestMap();
        var storage = server.System<EntityStorageSystem>();

        EntityUid console = default, pod = default, mob = default;
        await server.WaitPost(() =>
        {
            console = server.EntMan.SpawnEntity("StarHorizonDropPodConsole", map.GridCoords);
            pod = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords.Offset(new Vector2(2, 0)));
            mob = server.EntMan.SpawnEntity("DropPodTestPassenger", map.GridCoords.Offset(new Vector2(2, 0)));
            MakeFast(server.EntMan.GetComponent<DropPodComponent>(pod));
            storage.OpenStorage(pod);
            storage.Insert(mob, pod);
            storage.CloseStorage(pod);
        });

        Assert.That(server.EntMan.GetComponent<EntityStorageComponent>(pod).Contents.Contains(mob), "пассажир должен быть внутри капсулы");

        var target = new Vector2(80, 80);
        await server.WaitPost(() =>
        {
            var msg = new DropPodLaunchMessage(target);
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) msg);
        });
        await pair.RunSeconds(0.2f);

        var comp = server.EntMan.GetComponent<DropPodComponent>(pod);
        Assert.That(comp.State, Is.EqualTo(DropPodState.Countdown));
        Assert.That(comp.Marker, Is.Not.Null, "метка посадки должна появиться сразу");
        var markerPos = server.System<SharedTransformSystem>().GetWorldPosition(comp.Marker!.Value);
        Assert.That((markerPos - target).Length(), Is.LessThan(0.1f));

        var eye = server.EntMan.GetComponent<ContentEyeComponent>(mob);
        var appearance = server.System<SharedAppearanceSystem>();
        var xform = server.System<SharedTransformSystem>();

        // Камера отдаляется плавно, нарастает виньетка: посреди ZoomingOut зум между 1 и пиком.
        await pair.RunSeconds(1.0f + 0.5f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.ZoomingOut));
        Assert.That(eye.TargetZoom.X, Is.GreaterThan(1.05f).And.LessThan(comp.PeakZoom));
        var vignette = server.EntMan.GetComponent<DropPodVignetteComponent>(mob);
        Assert.That((vignette.From, vignette.To), Is.EqualTo((0f, 1f)), "виньетка нарастает");

        // В блюспейсе: капсула на FTL-карте и скрыта, зум на пике, виньетка полная.
        await pair.RunSeconds(0.8f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.InBluespace));
        Assert.That(server.EntMan.HasComponent<FTLMapComponent>(server.EntMan.GetComponent<TransformComponent>(pod).MapUid), "капсула летит по FTL-карте");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(server.EntMan.GetComponent<TransformComponent>(pod).MapUid), "пассажир там же");
        Assert.That(eye.TargetZoom.X, Is.EqualTo(comp.PeakZoom).Within(0.01f));
        Assert.That(vignette.To, Is.EqualTo(1f), "виньетка достигла максимума и держится");
        Assert.That(vignette.End, Is.LessThanOrEqualTo(server.Resolve<Robust.Shared.Timing.IGameTiming>().CurTime));
        Assert.That(appearance.TryGetData<bool>(pod, DropPodVisuals.InFlight, out var hidden) && hidden);
        Assert.That(server.EntMan.Deleted(comp.Marker), Is.False, "метка остаётся до посадки");

        // Конец БСС (после фазы прибытия FTL): телепорт, анимация падения, виньетка уходит в 0, камера возвращается.
        await pair.RunSeconds(1.7f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.Landing));
        Assert.That(server.EntMan.HasComponent<FTLMapComponent>(server.EntMan.GetComponent<TransformComponent>(pod).MapUid), Is.False);
        Assert.That((xform.GetWorldPosition(pod) - target).Length(), Is.LessThan(0.5f), "капсула в точке метки");
        Assert.That(appearance.TryGetData<bool>(pod, DropPodVisuals.InFlight, out hidden) && hidden, Is.False, "капсула появилась");
        Assert.That(server.EntMan.Deleted(comp.Marker), "метка исчезла");
        Assert.That((vignette.From, vignette.To), Is.EqualTo((1f, 0f)), "виньетка уменьшается до 0");
        Assert.That(eye.TargetZoom.X, Is.GreaterThan(1.0f).And.LessThan(comp.PeakZoom));

        await pair.RunSeconds(1.5f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.Idle));
        Assert.That(eye.TargetZoom.X, Is.EqualTo(1f).Within(0.01f));
        Assert.That(server.EntMan.HasComponent<DropPodVignetteComponent>(mob), Is.False);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaptureShuttleAndLandOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Error });
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, 1f);
        var map = await pair.CreateTestMap();
        var storage = server.System<EntityStorageSystem>();
        var xform = server.System<SharedTransformSystem>();

        // Захватываемый шаттл - сетка, которую потом "унесёт" в случайную точку.
        EntityUid shuttle = default, console = default, pod = default, wall = default;
        await server.WaitPost(() =>
        {
            shuttle = map.Grid.Owner;
            server.EntMan.EnsureComponent<ShuttleComponent>(shuttle);

            console = server.EntMan.SpawnEntity("StarHorizonDropPodConsole", map.GridCoords);
            pod = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords.Offset(new Vector2(2, 0)));
            MakeFast(server.EntMan.GetComponent<DropPodComponent>(pod));
            server.EntMan.GetComponent<DropPodConsoleComponent>(console).CaptureTime = TimeSpan.FromSeconds(1);
            wall = server.EntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(1, 1)));
        });

        Assert.That(new DropPodConsoleComponent().CaptureTime, Is.EqualTo(TimeSpan.FromSeconds(15)));

        await server.WaitPost(() =>
        {
            var msg = new DropPodCaptureMessage(server.EntMan.GetNetEntity(shuttle));
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) msg);
        });
        await pair.RunSeconds(0.3f);
        var consoleComp = server.EntMan.GetComponent<DropPodConsoleComponent>(console);
        Assert.That(consoleComp.CapturingGrid, Is.EqualTo(shuttle));
        Assert.That(consoleComp.CapturedGrid, Is.Null, "захват ещё не завершён");
        await pair.RunSeconds(1.0f);
        Assert.That(consoleComp.CapturedGrid, Is.EqualTo(shuttle));

        await server.WaitPost(() =>
        {
            var msg = new DropPodLaunchToCapturedMessage();
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) msg);
            storage.CloseStorage(pod);
        });

        // Шаттл улетает в произвольную точку, пока капсула в пути.
        await pair.RunSeconds(1.2f);
        await server.WaitPost(() => xform.SetWorldPosition(shuttle, new Vector2(900, -700)));
        await pair.RunSeconds(3.2f);

        var podComp = server.EntMan.GetComponent<DropPodComponent>(pod);
        Assert.That(podComp.State, Is.EqualTo(DropPodState.Landing));
        Assert.That(xform.GetParentUid(pod), Is.EqualTo(shuttle), "капсула должна оказаться на захваченном гриде");
        var world = xform.GetWorldPosition(pod);
        Assert.That((world - new Vector2(900, -700)).Length(), Is.LessThan(30f), "и лететь вместе с ним");

        // Не поверх закреплённой структуры.
        var wallPos = xform.GetWorldPosition(wall);
        Assert.That((world - wallPos).Length(), Is.GreaterThan(0.4f));

        await pair.CleanReturnAsync();
    }
}
