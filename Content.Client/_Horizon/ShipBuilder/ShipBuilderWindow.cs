using System.Linq;
using System.Numerics;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Maps;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._Horizon.ShipBuilder;

/// <summary>
/// Окно консоли верфи: слева меню (как спавн-меню) со списками тайлов и деталей из прототипа консоли
/// и чертежей, справа камера чертёжной карты.
/// </summary>
public sealed class ShipBuilderWindow : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    private readonly ShipBuilderView _view;
    private readonly Label _stats = new();
    private readonly Label _server = new();
    private readonly Button _rotateButton = new() { HorizontalExpand = true };
    private readonly Button _eraseButton = new() { HorizontalExpand = true };
    private readonly BoxContainer _tileList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _entityList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly LineEdit _search = new() { HorizontalExpand = true };

    private readonly List<Entry> _entries = new();
    private List<string> _knownTiles = new();
    private List<string> _knownEntities = new();

    private ShipBuilderTool _tool = ShipBuilderTool.Tile;
    private string _id = string.Empty;
    private int _rotation;

    private sealed class Entry
    {
        public required Control Control;
        public required Label Label;
        public required string Text;
        public required string SearchText;
        public required ShipBuilderTool Tool;
        public required string Id;
    }

    public event Action<Vector2, ShipBuilderTool, string, int>? OnPlace;
    public event Action<Vector2>? OnMoveCamera;

    public ShipBuilderWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("ship-builder-title");
        MinSize = new Vector2(820, 560);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        Contents.AddChild(root);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SetWidth = 280,
            Margin = new Thickness(0, 0, 6, 0),
        };
        root.AddChild(side);

        var tools = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _eraseButton.Text = Loc.GetString("ship-builder-tool-erase");
        _eraseButton.OnPressed += _ => Select(ShipBuilderTool.Erase, string.Empty);
        _rotateButton.OnPressed += _ =>
        {
            _rotation = (_rotation + 90) % 360;
            UpdateRotation();
        };
        tools.AddChild(_eraseButton);
        tools.AddChild(_rotateButton);
        side.AddChild(tools);

        side.AddChild(new Label { Text = Loc.GetString("ship-builder-tool-tile") });
        side.AddChild(new ScrollContainer { SetHeight = 110, Children = { _tileList } });

        side.AddChild(new Label { Text = Loc.GetString("ship-builder-tool-entity") });
        _search.PlaceHolder = Loc.GetString("ship-builder-search");
        _search.OnTextChanged += args => ApplyFilter(args.Text);
        side.AddChild(_search);
        side.AddChild(new ScrollContainer { VerticalExpand = true, Children = { _entityList } });

        side.AddChild(_stats);
        side.AddChild(_server);
        foreach (var line in Loc.GetString("ship-builder-hint").Split('|'))
        {
            side.AddChild(new Label { Text = line, FontColorOverride = Color.Gray });
        }

        _view = new ShipBuilderView();
        root.AddChild(_view);

        _view.OnPaint += world =>
        {
            // Без выбранного элемента кликать нечем.
            if (_tool != ShipBuilderTool.Erase && string.IsNullOrEmpty(_id))
                return;

            OnPlace?.Invoke(world, _tool, _id, _rotation);
        };
        _view.OnPan += delta => OnMoveCamera?.Invoke(delta);

        UpdateRotation();
    }

    private void UpdateRotation()
    {
        _rotateButton.Text = Loc.GetString("ship-builder-rotation", ("degrees", _rotation));
    }

    private void Select(ShipBuilderTool tool, string id)
    {
        _tool = tool;
        _id = id;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _eraseButton.Text = (_tool == ShipBuilderTool.Erase ? "> " : string.Empty) + Loc.GetString("ship-builder-tool-erase");

        foreach (var entry in _entries)
        {
            var selected = entry.Tool == _tool && entry.Id == _id;
            entry.Label.Text = selected ? $"> {entry.Text}" : entry.Text;
        }
    }

    private void ApplyFilter(string text)
    {
        foreach (var entry in _entries)
        {
            if (entry.Tool != ShipBuilderTool.Entity)
                continue;

            entry.Control.Visible = string.IsNullOrWhiteSpace(text)
                                    || entry.SearchText.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RebuildTiles(List<string> ids)
    {
        _tileList.RemoveAllChildren();
        _entries.RemoveAll(e => e.Tool == ShipBuilderTool.Tile);

        foreach (var id in ids)
        {
            var name = _proto.TryIndex<ContentTileDefinition>(id, out var def) ? Loc.GetString(def.Name) : id;
            AddEntry(_tileList, ShipBuilderTool.Tile, id, name, id, null);
        }
    }

    private void RebuildEntities(List<string> ids)
    {
        _entityList.RemoveAllChildren();
        _entries.RemoveAll(e => e.Tool == ShipBuilderTool.Entity);

        foreach (var id in ids)
        {
            var name = _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
            AddEntry(_entityList, ShipBuilderTool.Entity, id, name, $"{name} {id}", id);
        }

        ApplyFilter(_search.Text);
    }

    private void AddEntry(Control parent, ShipBuilderTool tool, string id, string text, string searchText, string? iconProto)
    {
        var button = new ContainerButton { HorizontalExpand = true };
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        button.AddChild(row);

        if (iconProto != null)
        {
            row.AddChild(new EntityPrototypeView(iconProto, _entManager)
            {
                MinSize = new Vector2(32, 32),
                SetSize = new Vector2(32, 32),
            });
        }

        var label = new Label { Text = text, VerticalAlignment = VAlignment.Center, ClipText = true, HorizontalExpand = true };
        row.AddChild(label);

        button.OnPressed += _ => Select(tool, id);
        parent.AddChild(button);

        _entries.Add(new Entry
        {
            Control = button,
            Label = label,
            Text = text,
            SearchText = searchText,
            Tool = tool,
            Id = id,
        });
    }

    public void UpdateState(ShipBuilderBuiState state)
    {
        if (!_knownTiles.SequenceEqual(state.TileIds))
        {
            _knownTiles = new List<string>(state.TileIds);
            RebuildTiles(_knownTiles);
        }

        if (!_knownEntities.SequenceEqual(state.EntityIds))
        {
            _knownEntities = new List<string>(state.EntityIds);
            RebuildEntities(_knownEntities);
        }

        // Если выбранного больше нет (или ничего не выбрано) - берём первый тайл.
        var stillValid = _tool == ShipBuilderTool.Erase
                         || (_tool == ShipBuilderTool.Tile && _knownTiles.Contains(_id))
                         || (_tool == ShipBuilderTool.Entity && _knownEntities.Contains(_id));
        if (!stillValid)
        {
            _tool = ShipBuilderTool.Tile;
            _id = _knownTiles.Count > 0 ? _knownTiles[0] : string.Empty;
        }

        UpdateSelection();

        _stats.Text = Loc.GetString("ship-builder-stats", ("tiles", state.Tiles), ("objects", state.Objects));
        _server.Text = Loc.GetString(state.ServerLinked ? "ship-builder-server-linked" : "ship-builder-server-missing");
        _view.SetCamera(state.Camera);
    }

    public override void Close()
    {
        _view.Release();
        base.Close();
    }
}
