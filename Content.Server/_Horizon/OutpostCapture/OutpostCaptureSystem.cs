using Content.Shared._Horizon.FlavorText;
using Content.Shared._Horizon.OutpostCapture;
using Content.Shared._Horizon.OutpostCapture.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Horizon.OutpostCapture;

public sealed class OutpostCaptureSystem : SharedOutpostCaptureSystem
{
    [Dependency] private readonly SharedShuttleSystem _shuttleSystem = null!;

    public override void Initialize() {} // Тут ничего нет, потому, что у нас дублируются подписки при наследовании! А мне они тут не нужны!

    private bool TrySetCapturedFaction(EntityUid uid, OutpostCaptureComponent outpost)
    {
        if (outpost.CapturedFaction != null)
            return true;

        if (outpost.CapturedConsoles.Count == 0)
            return false;

        List<string> factionList = [];
        foreach (var console in outpost.CapturedConsoles)
        {
            if (!TryGetEntity(console, out var actualConsole)
                || !TryComp<OutpostConsoleComponent>(actualConsole, out var consoleComp))
                continue;

            if (consoleComp.CapturedFaction == null)
                return false;

            if (factionList.Contains(consoleComp.CapturedFaction))
                continue;

            factionList.Add(consoleComp.CapturedFaction);
        }

        if (factionList.Count > 1 ||
            !PrototypeManager.TryIndex<CharacterFactionPrototype>(factionList[0], out var faction))
            return false;

        if (faction.OutpostSpawnListProto == null)
        {
            Sawmill.Error($"{faction.ID} имеет пустой список для спавна, после захвата. Стоит проверить правильность прототипа.");
            return false;
        }

        EnsureComp<IFFComponent>(uid, out var iff); // I just need this comp in grid!
        _shuttleSystem.SetIFFColor(uid, faction.Color, iff);
        outpost.SpawnList = faction.OutpostSpawnListProto.Value;
        outpost.CapturedFaction = faction.ID;
        Dirty(uid, outpost);
        return true;
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        var secondPast = TimeSpan.FromSeconds(deltaTime);
        var enumerable = EntityManager.EntityQueryEnumerator<OutpostCaptureComponent>();
        while (enumerable.MoveNext(out var uid, out var outpost))
        {
            if (outpost.SpawnLocation == null ||
                !PrototypeManager.TryIndex(outpost.SpawnList, out var index))
                continue;

            UpdateOutpostConsoles(outpost, secondPast);
            if (outpost.CapturedConsoles.Count < outpost.NeedCaptured)
            {
                outpost.NextSpawn = outpost.SpawnCooldown;
                outpost.CapturedFaction = null;
                continue;
            }

            if (!TrySetCapturedFaction(uid, outpost))
                continue;

            outpost.NextSpawn -= secondPast;
            if (outpost.NextSpawn > TimeSpan.Zero)
                continue;

            TrySpawnItemsInList(index, outpost.SpawnLocation.Value);
            outpost.NextSpawn = outpost.SpawnCooldown;
        }
    }

    private void UpdateOutpostConsoles(OutpostCaptureComponent outpost, TimeSpan deltaTime)
    {
        if (outpost.CapturingConsoles.Count == 0)
            return;

        var updatedCapturedConsoles = new List<NetEntity>(outpost.CapturedConsoles);
        var updatedCapturingConsoles = new List<NetEntity>(outpost.CapturingConsoles);
        foreach (var console in outpost.CapturingConsoles)
        {
            if (!TryGetEntity(console, out var actualConsole)
                || !TryComp<OutpostConsoleComponent>(actualConsole, out var consoleComp))
                continue;

            consoleComp.CapturingTime -= deltaTime;
            if (UiSystem.HasUi(actualConsole.Value, CaptureUIKey.Key))
            {
                var message = new ProgressBarUpdate(UpdateProgressBar((actualConsole.Value, consoleComp)));
                UiSystem.ServerSendUiMessage(actualConsole.Value, CaptureUIKey.Key, message);
            }

            if (consoleComp.CapturingTime > TimeSpan.Zero)
                continue;

            consoleComp.CapturingTime = null;
            updatedCapturedConsoles.Add(console); // Already captured.
            updatedCapturingConsoles.Remove(console); // So if captured don`t capturing again
            consoleComp.State = OutpostConsoleState.Captured;
            Dirty(actualConsole.Value, consoleComp);
        }

        outpost.CapturedConsoles = updatedCapturedConsoles;
        outpost.CapturingConsoles = updatedCapturingConsoles;
    }

    private void TrySpawnItemsInList(OutpostSpawnPrototype list, EntityCoordinates location)
    {
        try
        {
            foreach (var entity in list.SpawnList)
            {
                var amount = entity.Amount;
                if (!Random.Prob(entity.SpawnProbability))
                    continue;

                while (amount > 0)
                {
                    SpawnAtPosition(entity.PrototypeId, location);
                    amount--;
                }
            }
        }
        catch (Exception e)
        {
            Sawmill.Error(e.Message);
        }
    }
}
