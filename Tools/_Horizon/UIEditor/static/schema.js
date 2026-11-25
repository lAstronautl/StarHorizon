/* Описание элементов управления Robust UI / SS14 для редактора.
 * Добавить свой контрол: допишите запись в CONTROLS (и при необходимости namespace в NAMESPACES).
 */
"use strict";

const NAMESPACES = {
    "": "https://spacestation14.io",
    x: "http://schemas.microsoft.com/winfx/2006/xaml",
    controls: "clr-namespace:Content.Client.UserInterface.Controls",
    gfx: "clr-namespace:Robust.Client.Graphics;assembly=Robust.Client",
    cc: "clr-namespace:Content.Client.Administration.UI.CustomControls",
    style: "clr-namespace:Content.Client.Stylesheets",
};

const ENUMS = {
    Access: ["", "Public", "Internal", "Protected", "Private"],
    HAlign: ["", "Stretch", "Left", "Center", "Right"],
    VAlign: ["", "Stretch", "Top", "Center", "Bottom"],
    Orientation: ["", "Horizontal", "Vertical"],
    BoxAlign: ["", "Begin", "Center", "End"],
    LabelAlign: ["", "Left", "Center", "Right", "Fill"],
    LabelVAlign: ["", "Top", "Center", "Bottom", "Fill"],
    TextAlign: ["", "Left", "Center", "Right"],
    MouseFilter: ["", "Stop", "Pass", "Ignore"],
    Bool: ["", "True", "False"],
    Stretch: ["", "Scale", "Tile", "Keep", "KeepCentered", "KeepAspect", "KeepAspectCentered", "KeepAspectCovered"],
    SelectMode: ["", "None", "Single", "Multiple", "Button"],
    Direction: ["", "South", "North", "East", "West"],
};

const STYLE_CLASSES = [
    "LabelHeading", "LabelHeadingBigger", "LabelSubText", "LabelKeyText", "LabelBig", "LabelSmall",
    "LabelSecondaryColor", "StatusFieldTitle", "WindowFooterText", "monospace", "bold",
    "OpenLeft", "OpenRight", "OpenBoth", "ButtonSquare", "ButtonBig", "ButtonColorRed", "ButtonColorGreen", "Caution",
    "LowDivider", "HighDivider", "AngleRect", "BackgroundDark", "Inset", "PanelBackgroundLight",
    "PanelBackgroundBaseDark", "WindowHeadingBackground", "transparentItemList", "transparentBackgroundItemList",
];

// Свойства, общие для всех Control
const COMMON_PROPS = [
    { group: "Общее", name: "Name", type: "string", hint: "имя поля в C# (GenerateTypedNameReferences)" },
    { group: "Общее", name: "Access", type: "enum", values: ENUMS.Access },
    { group: "Общее", name: "Visible", type: "bool" },
    { group: "Общее", name: "ToolTip", type: "loc" },
    { group: "Общее", name: "StyleClasses", type: "styleclasses" },
    { group: "Общее", name: "MouseFilter", type: "enum", values: ENUMS.MouseFilter },
    { group: "Общее", name: "Modulate", type: "color" },
    { group: "Раскладка", name: "HorizontalExpand", type: "bool" },
    { group: "Раскладка", name: "VerticalExpand", type: "bool" },
    { group: "Раскладка", name: "SizeFlagsStretchRatio", type: "float" },
    { group: "Раскладка", name: "HorizontalAlignment", type: "enum", values: ENUMS.HAlign },
    { group: "Раскладка", name: "VerticalAlignment", type: "enum", values: ENUMS.VAlign },
    { group: "Раскладка", name: "Margin", type: "thickness", hint: "a | гор верт | лев верх прав низ" },
    { group: "Размер", name: "MinSize", type: "vec2" },
    { group: "Размер", name: "SetSize", type: "vec2" },
    { group: "Размер", name: "MaxSize", type: "vec2" },
    { group: "Размер", name: "MinWidth", type: "float" },
    { group: "Размер", name: "MinHeight", type: "float" },
    { group: "Размер", name: "SetWidth", type: "float" },
    { group: "Размер", name: "SetHeight", type: "float" },
    { group: "Размер", name: "MaxWidth", type: "float" },
    { group: "Размер", name: "MaxHeight", type: "float" },
    { group: "Размер", name: "RectClipContent", type: "bool" },
];

const P = {
    text: { name: "Text", type: "loc" },
    disabled: { name: "Disabled", type: "bool" },
    toggle: { name: "ToggleMode", type: "bool" },
    pressed: { name: "Pressed", type: "bool" },
    clip: { name: "ClipText", type: "bool" },
    textAlign: { name: "TextAlign", type: "enum", values: ENUMS.TextAlign },
    orientation: { name: "Orientation", type: "enum", values: ENUMS.Orientation },
    title: { name: "Title", type: "loc" },
    min: { name: "MinValue", type: "float" },
    max: { name: "MaxValue", type: "float" },
    value: { name: "Value", type: "float" },
    tex: { name: "TexturePath", type: "texture", hint: "/Textures/..." },
};

// children: "none" | "single" | "multi"
const CONTROLS = [
    // --- Окна (корневые) ---
    { tag: "controls:FancyWindow", cat: "Окна", children: "multi", root: true, base: "FancyWindow",
      using: "Content.Client.UserInterface.Controls",
      defaults: { Title: "{Loc 'my-window-title'}", MinSize: "400 300" },
      props: [P.title, { name: "Resizable", type: "bool" }] },
    { tag: "DefaultWindow", cat: "Окна", children: "multi", root: true, base: "DefaultWindow",
      using: "Robust.Client.UserInterface.CustomControls",
      defaults: { Title: "{Loc 'my-window-title'}", MinSize: "400 300" },
      props: [P.title, { name: "Resizable", type: "bool" }] },

    // --- Контейнеры ---
    { tag: "BoxContainer", cat: "Контейнеры", children: "multi", defaults: { Orientation: "Vertical" },
      props: [P.orientation, { name: "SeparationOverride", type: "int" }, { name: "Align", type: "enum", values: ENUMS.BoxAlign }] },
    { tag: "GridContainer", cat: "Контейнеры", children: "multi", defaults: { Columns: "2" },
      props: [{ name: "Columns", type: "int" }, { name: "Rows", type: "int" },
              { name: "HSeparationOverride", type: "int" }, { name: "VSeparationOverride", type: "int" }] },
    { tag: "PanelContainer", cat: "Контейнеры", children: "multi", panelOverride: true, props: [] },
    { tag: "ScrollContainer", cat: "Контейнеры", children: "single", defaults: { HScrollEnabled: "False", VerticalExpand: "True" },
      props: [{ name: "HScrollEnabled", type: "bool" }, { name: "VScrollEnabled", type: "bool" }, { name: "ReturnMeasure", type: "bool" }] },
    { tag: "SplitContainer", cat: "Контейнеры", children: "multi", defaults: { Orientation: "Horizontal" },
      props: [P.orientation, { name: "SplitWidth", type: "float" }] },
    { tag: "TabContainer", cat: "Контейнеры", children: "multi", props: [{ name: "CurrentTab", type: "int" }] },
    { tag: "Control", cat: "Контейнеры", children: "multi", props: [] },
    { tag: "LayoutContainer", cat: "Контейнеры", children: "multi", props: [] },
    { tag: "controls:StripeBack", cat: "Контейнеры", children: "multi", props: [{ name: "HasTopEdge", type: "bool" }, { name: "HasBottomEdge", type: "bool" }] },
    { tag: "ContainerButton", cat: "Контейнеры", children: "multi", props: [P.disabled, P.toggle, P.pressed] },
    { tag: "Collapsible", cat: "Контейнеры", children: "multi", defaults: { Orientation: "Vertical" }, props: [P.orientation, { name: "BodyVisible", type: "bool" }] },
    { tag: "CollapsibleHeading", cat: "Контейнеры", children: "none", defaults: { Title: "Заголовок" }, props: [P.title] },
    { tag: "CollapsibleBody", cat: "Контейнеры", children: "multi", props: [] },

    // --- Текст ---
    { tag: "Label", cat: "Текст", children: "none", defaults: { Text: "Label" },
      props: [P.text, { name: "Align", type: "enum", values: ENUMS.LabelAlign }, { name: "VAlign", type: "enum", values: ENUMS.LabelVAlign },
              { name: "FontColorOverride", type: "color" }, P.clip] },
    { tag: "RichTextLabel", cat: "Текст", children: "none", defaults: { Text: "[bold]Rich[/bold] text" }, props: [P.text] },

    // --- Кнопки и ввод ---
    { tag: "Button", cat: "Ввод", children: "none", defaults: { Text: "Button" },
      props: [P.text, P.textAlign, P.disabled, P.toggle, P.pressed, P.clip] },
    { tag: "controls:ConfirmButton", cat: "Ввод", children: "none", defaults: { Text: "Confirm" },
      props: [P.text, { name: "ConfirmationText", type: "loc" }, P.textAlign, P.disabled] },
    { tag: "TextureButton", cat: "Ввод", children: "none", defaults: { MinSize: "24 24" },
      props: [P.tex, { name: "Scale", type: "vec2" }, P.disabled, P.toggle] },
    { tag: "CheckBox", cat: "Ввод", children: "none", defaults: { Text: "CheckBox" }, props: [P.text, P.pressed, P.disabled] },
    { tag: "OptionButton", cat: "Ввод", children: "none", props: [P.disabled, P.textAlign] },
    { tag: "LineEdit", cat: "Ввод", children: "none", defaults: { HorizontalExpand: "True" },
      props: [P.text, { name: "PlaceHolder", type: "loc" }, { name: "Editable", type: "bool" }] },
    { tag: "TextEdit", cat: "Ввод", children: "none", defaults: { MinHeight: "60" }, props: [{ name: "Placeholder", type: "loc" }, { name: "Editable", type: "bool" }] },
    { tag: "SpinBox", cat: "Ввод", children: "none", props: [P.value] },
    { tag: "FloatSpinBox", cat: "Ввод", children: "none", props: [P.value, { name: "Step", type: "float" }] },
    { tag: "Slider", cat: "Ввод", children: "none", defaults: { MinValue: "0", MaxValue: "100", Value: "50", HorizontalExpand: "True" }, props: [P.min, P.max, P.value] },
    { tag: "ItemList", cat: "Ввод", children: "none", defaults: { VerticalExpand: "True" }, props: [{ name: "SelectMode", type: "enum", values: ENUMS.SelectMode }] },

    // --- Графика ---
    { tag: "TextureRect", cat: "Графика", children: "none", defaults: { TexturePath: "/Textures/Interface/Nano/gear.svg.192dpi.png", SetSize: "32 32", Stretch: "KeepAspectCentered" },
      props: [P.tex, { name: "Stretch", type: "enum", values: ENUMS.Stretch }, { name: "TextureScale", type: "vec2" }] },
    { tag: "SpriteView", cat: "Графика", children: "none", defaults: { SetSize: "64 64" },
      props: [{ name: "Scale", type: "vec2" }, { name: "OverrideDirection", type: "enum", values: ENUMS.Direction }] },
    { tag: "ProgressBar", cat: "Графика", children: "none", defaults: { MinValue: "0", MaxValue: "1", Value: "0.5", MinHeight: "16", HorizontalExpand: "True" }, props: [P.min, P.max, P.value] },
    { tag: "controls:HLine", cat: "Графика", children: "none", defaults: { Color: "#A88B5E", Thickness: "2" }, props: [{ name: "Color", type: "color" }, { name: "Thickness", type: "float" }] },
    { tag: "cc:HSeparator", cat: "Графика", children: "none", props: [] },
    { tag: "cc:VSeparator", cat: "Графика", children: "none", props: [] },
];

const CONTROL_MAP = {};
for (const c of CONTROLS) {
    CONTROL_MAP[c.tag] = c;
    CONTROL_MAP[c.tag.split(":").pop()] = CONTROL_MAP[c.tag.split(":").pop()] || c;
}

// Свойства StyleBoxFlat (PanelContainer.PanelOverride)
const STYLEBOX_PROPS = [
    { name: "BackgroundColor", type: "color" },
    { name: "BorderColor", type: "color" },
    { name: "BorderThickness", type: "thickness" },
    { name: "ContentMarginLeftOverride", type: "float" },
    { name: "ContentMarginTopOverride", type: "float" },
    { name: "ContentMarginRightOverride", type: "float" },
    { name: "ContentMarginBottomOverride", type: "float" },
];

// Цвета темы Nano (примерные, для предпросмотра)
const THEME = {
    windowBg: "#25252a",
    windowHeader: "#1b1b1e",
    windowBorder: "#3b3e56",
    accentGold: "#a88b5e",
    text: "#e8e8e8",
    subText: "#b4b4b4",
    button: "#464b46",
    buttonRed: "#d43b3b",
    buttonCaution: "#ab3232",
    buttonGood: "#3e6c45",
    buttonPressed: "#3e6c45",
    buttonDisabled: "#303331",
    lineEdit: "#323232",
    panelDark: "#1e201e",
    darkBg: "#202025",
};

const TEMPLATES = [
    { name: "FancyWindow — пустое окно", xaml:
`<controls:FancyWindow xmlns="https://spacestation14.io"
                      xmlns:controls="clr-namespace:Content.Client.UserInterface.Controls"
                      Title="{Loc 'my-window-title'}"
                      MinSize="400 300">
    <BoxContainer Orientation="Vertical" Margin="4">
    </BoxContainer>
</controls:FancyWindow>
` },
    { name: "FancyWindow — консоль со списком и кнопками", xaml:
`<controls:FancyWindow xmlns="https://spacestation14.io"
                      xmlns:controls="clr-namespace:Content.Client.UserInterface.Controls"
                      xmlns:gfx="clr-namespace:Robust.Client.Graphics;assembly=Robust.Client"
                      Title="{Loc 'my-console-title'}"
                      MinSize="600 400">
    <BoxContainer Orientation="Vertical" Margin="4">
        <BoxContainer Orientation="Horizontal">
            <Label Text="{Loc 'my-console-status'}" StyleClasses="LabelHeading"/>
            <Control HorizontalExpand="True"/>
            <Label Name="StatusLabel" Text="OK" FontColorOverride="#31843E"/>
        </BoxContainer>
        <PanelContainer StyleClasses="LowDivider" Margin="0 4"/>
        <BoxContainer Orientation="Horizontal" VerticalExpand="True">
            <PanelContainer HorizontalExpand="True" VerticalExpand="True">
                <PanelContainer.PanelOverride>
                    <gfx:StyleBoxFlat BackgroundColor="#1B1B1E"/>
                </PanelContainer.PanelOverride>
                <ScrollContainer HScrollEnabled="False" VerticalExpand="True">
                    <BoxContainer Name="EntriesContainer" Orientation="Vertical" HorizontalExpand="True"/>
                </ScrollContainer>
            </PanelContainer>
            <BoxContainer Orientation="Vertical" MinWidth="150" Margin="4 0 0 0">
                <Button Name="ActionButton" Text="{Loc 'my-console-action'}"/>
                <Button Name="RefreshButton" Text="{Loc 'my-console-refresh'}"/>
                <Control VerticalExpand="True"/>
                <controls:ConfirmButton Name="ResetButton" Text="{Loc 'my-console-reset'}"/>
            </BoxContainer>
        </BoxContainer>
        <controls:StripeBack Margin="0 4 0 0">
            <Label Text="{Loc 'my-console-footer'}" StyleClasses="WindowFooterText" Align="Center"/>
        </controls:StripeBack>
    </BoxContainer>
</controls:FancyWindow>
` },
    { name: "Форма настроек (GridContainer)", xaml:
`<controls:FancyWindow xmlns="https://spacestation14.io"
                      xmlns:controls="clr-namespace:Content.Client.UserInterface.Controls"
                      Title="{Loc 'my-settings-title'}"
                      MinSize="360 220">
    <BoxContainer Orientation="Vertical" Margin="8">
        <GridContainer Columns="2" HSeparationOverride="8" VSeparationOverride="4">
            <Label Text="Имя:"/>
            <LineEdit Name="NameEdit" HorizontalExpand="True" PlaceHolder="введите имя"/>
            <Label Text="Режим:"/>
            <OptionButton Name="ModeOption" HorizontalExpand="True"/>
            <Label Text="Мощность:"/>
            <Slider Name="PowerSlider" MinValue="0" MaxValue="100" Value="50" HorizontalExpand="True"/>
            <Label Text="Включено:"/>
            <CheckBox Name="EnabledCheck" Text="Да"/>
        </GridContainer>
        <Control VerticalExpand="True"/>
        <BoxContainer Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Name="CancelButton" Text="Отмена" StyleClasses="OpenRight"/>
            <Button Name="ApplyButton" Text="Применить" StyleClasses="OpenLeft"/>
        </BoxContainer>
    </BoxContainer>
</controls:FancyWindow>
` },
    { name: "DefaultWindow — пустое окно", xaml:
`<DefaultWindow xmlns="https://spacestation14.io"
               Title="{Loc 'my-window-title'}"
               MinSize="400 300">
    <BoxContainer Orientation="Vertical">
    </BoxContainer>
</DefaultWindow>
` },
    { name: "Элемент (Control) — для вставки в другие окна", xaml:
`<BoxContainer xmlns="https://spacestation14.io"
              Orientation="Horizontal"
              HorizontalExpand="True">
    <Label Name="TitleLabel" Text="Элемент" HorizontalExpand="True"/>
    <Button Name="SelectButton" Text="Выбрать"/>
</BoxContainer>
` },
];
