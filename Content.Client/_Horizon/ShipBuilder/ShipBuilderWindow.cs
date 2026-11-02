using System.Numerics;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Maps;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._Horizon.ShipBuilder;

/// <summary>Окно консоли верфи: палитра слева, камера чертёжной карты справа.</summary>
public sealed class ShipBuilderWindow : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly ShipBuilderView _view;
    private readonly Label _stats = new();
    private readonly List<(Button Button, string Label, ShipBuilderTool Tool, string Id)> _buttons = new();

    private ShipBuilderTool _tool = ShipBuilderTool.Tile;
    private string _id = ShipBuilderPalette.Tiles[0];

    public event Action<Vector2, ShipBuilderTool, string>? OnPlace;
    public event Action<Vector2>? OnMoveCamera;

    public ShipBuilderWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("ship-builder-title");
        MinSize = new Vector2(760, 540);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        Contents.AddChild(root);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SetWidth = 230,
            Margin = new Thickness(0, 0, 6, 0),
        };
        root.AddChild(side);

        AddButton(side, Loc.GetString("ship-builder-tool-erase"), ShipBuilderTool.Erase, string.Empty);

        side.AddChild(new Label { Text = Loc.GetString("ship-builder-tool-tile") });
        var tiles = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        foreach (var id in ShipBuilderPalette.Tiles)
        {
            var name = _proto.TryIndex<ContentTileDefinition>(id, out var def) ? Loc.GetString(def.Name) : id;
            AddButton(tiles, name, ShipBuilderTool.Tile, id);
        }

        side.AddChild(new ScrollContainer { VerticalExpand = true, Children = { tiles } });

        side.AddChild(new Label { Text = Loc.GetString("ship-builder-tool-wall") });
        foreach (var id in ShipBuilderPalette.Walls)
        {
            var name = _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
            AddButton(side, name, ShipBuilderTool.Wall, id);
        }

        side.AddChild(_stats);
        foreach (var line in Loc.GetString("ship-builder-hint").Split('|'))
        {
            side.AddChild(new Label { Text = line, FontColorOverride = Color.Gray });
        }

        _view = new ShipBuilderView();
        root.AddChild(_view);

        _view.OnPaint += world => OnPlace?.Invoke(world, _tool, _id);
        _view.OnPan += delta => OnMoveCamera?.Invoke(delta);

        UpdateSelection();
    }

    private void AddButton(Control parent, string label, ShipBuilderTool tool, string id)
    {
        var button = new Button { Text = label, HorizontalExpand = true };
        button.OnPressed += _ =>
        {
            _tool = tool;
            _id = id;
            UpdateSelection();
        };
        parent.AddChild(button);
        _buttons.Add((button, label, tool, id));
    }

    private void UpdateSelection()
    {
        foreach (var (button, label, tool, id) in _buttons)
        {
            button.Text = tool == _tool && id == _id ? $"> {label}" : label;
        }
    }

    public void UpdateState(ShipBuilderBuiState state)
    {
        _stats.Text = Loc.GetString("ship-builder-stats", ("tiles", state.Tiles), ("walls", state.Walls));
        _view.SetCamera(state.Camera);
    }

    public override void Close()
    {
        _view.Release();
        base.Close();
    }
}
