using Content.Shared._Horizon.StationDeployment;
using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared.Station.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Configuration;

namespace Content.Server._Horizon.StationDeployment;

/// <summary>
/// Lets a player rename the station a <see cref="StationControlConsoleComponent"/> belongs to.
/// The station starts out with a random name/code (assigned by StationNameSetup on deployment).
/// </summary>
public sealed class StationControlConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private int _maxNameLength;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationControlConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StationControlConsoleComponent, StationControlConsoleRenameMessage>(OnRename);

        Subs.CVar(_cfg, CCVars.MaxNameLength, value => _maxNameLength = value, true);
    }

    private void OnUiOpened(Entity<StationControlConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnRename(Entity<StationControlConsoleComponent> ent, ref StationControlConsoleRenameMessage args)
    {
        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station)
            return;

        var baseName = args.Name.Trim();
        if (baseName.Length == 0 || baseName.Length > _maxNameLength)
        {
            _popup.PopupEntity(Loc.GetString("station-control-console-rename-invalid"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        // The station's number/code is assigned once on deployment and never changes - only the
        // base name the player picks is editable here. The deed lives on the station's grid(s),
        // not on the abstract station entity.
        var number = FindStationDeed(station)?.StationNumber;
        var fullName = number is null ? baseName : $"{baseName} {number}";

        _station.RenameStation(station, fullName, loud: false);

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                _metaData.SetEntityName(grid, fullName);

                if (TryComp<StationDeedComponent>(grid, out var gridDeed))
                    gridDeed.StationName = baseName;
            }
        }

        _popup.PopupEntity(Loc.GetString("station-control-console-rename-success"), ent, args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void UpdateUi(Entity<StationControlConsoleComponent> ent)
    {
        var station = _station.GetOwningStation(ent.Owner);
        var name = station is { Valid: true } stationUid
            ? FindStationDeed(stationUid)?.StationName ?? MetaData(stationUid).EntityName
            : string.Empty;

        _uiSystem.SetUiState(ent.Owner, StationControlConsoleUiKey.Key, new StationControlConsoleBuiState(name));
    }

    /// <summary>
    /// Finds the <see cref="StationDeedComponent"/> tracking this station's number/code - it lives on
    /// one of the station's grids rather than on the abstract station entity itself.
    /// </summary>
    private StationDeedComponent? FindStationDeed(EntityUid station)
    {
        if (!TryComp<StationDataComponent>(station, out var stationData))
            return null;

        foreach (var grid in stationData.Grids)
        {
            if (TryComp<StationDeedComponent>(grid, out var deed))
                return deed;
        }

        return null;
    }
}
