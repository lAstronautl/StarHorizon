using System.Numerics;
using Content.Client.Eui;
using Content.Shared._Horizon.ShipBuilder;
using Content.Shared.Eui;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._Horizon.ShipBuilder;

[UsedImplicitly]
public sealed class ShipBuilderEui : BaseEui
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly DefaultWindow _window;
    private readonly ShipBuilderView _view;
    private readonly Label _stats = new();
    private readonly List<(Button Button, string Label, ShipBuilderTool Tool, string Id)> _buttons = new();

    private ShipBuilderTool _tool = ShipBuilderTool.Tile;
    private string _id = ShipBuilderPalette.Tiles[0];

    public ShipBuilderEui()
    {
        IoCManager.InjectDependencies(this);

        _window = new DefaultWindow
        {
            Title = Loc.GetString("ship-builder-title"),
            MinSize = new Vector2(720, 520),
        };

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _window.ContentsContainer.AddChild(root);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 200,
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
        side.AddChild(new Label
        {
            Text = Loc.GetString("ship-builder-hint"),
            FontColorOverride = Color.Gray,
        });

        _view = new ShipBuilderView();
        root.AddChild(_view);

        _view.OnPaint += world => SendMessage(new ShipBuilderPlaceMessage(world, _tool, _id));
        _view.OnPan += delta => SendMessage(new ShipBuilderMoveCameraMessage(delta));

        _window.OnClose += () => SendMessage(new CloseEuiMessage());

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

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _view.Release();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not ShipBuilderEuiState s)
            return;

        _stats.Text = Loc.GetString("ship-builder-stats", ("tiles", s.Tiles), ("walls", s.Walls));
        _view.SetState(s.Camera, s.Tick);
    }
}
