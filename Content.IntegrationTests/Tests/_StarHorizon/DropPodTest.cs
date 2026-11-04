using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._StarHorizon.DropPod;
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
        pod.ZoomInTime = Fast;
    }

    [Test]
    public async Task LaunchToCoordinates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Error });
        var server = pair.Server;
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

        // Камера отдаляется плавно: посреди ZoomingOut зум между 1 и пиком.
        await pair.RunSeconds(1.0f + 0.5f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.ZoomingOut));
        var zoom = server.EntMan.GetComponent<ContentEyeComponent>(mob).TargetZoom.X;
        Assert.That(zoom, Is.GreaterThan(1.05f).And.LessThan(comp.PeakZoom));

        // В блюспейсе капсулы нет на карте, зум на пике.
        await pair.RunSeconds(0.9f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.InBluespace));
        Assert.That(server.EntMan.GetComponent<ContentEyeComponent>(mob).TargetZoom.X, Is.EqualTo(comp.PeakZoom).Within(0.01f));

        // Приземление.
        await pair.RunSeconds(1.0f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.Landing));
        var landed = server.System<SharedTransformSystem>().GetWorldPosition(pod);
        Assert.That((landed - target).Length(), Is.LessThan(0.5f), "капсула должна приземлиться в точке метки");
        Assert.That(server.EntMan.Deleted(comp.Marker), "метка должна исчезнуть");

        // Камера вернулась.
        await pair.RunSeconds(1.5f);
        Assert.That(comp.State, Is.EqualTo(DropPodState.Idle));
        Assert.That(server.EntMan.GetComponent<ContentEyeComponent>(mob).TargetZoom.X, Is.EqualTo(1f).Within(0.01f));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaptureShuttleAndLandOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Error });
        var server = pair.Server;
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
        await pair.RunSeconds(2.2f);

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
