// Модуль «Элементы»: изменение и чтение контролов окна.
"use strict";
(() => {
    const TEXTED = ["Label", "Button", "ConfirmButton", "CheckBox", "LineEdit", "RichTextLabel", "TextEdit"];
    const BTN = ["Button", "ConfirmButton", "TextureButton", "CheckBox", "OptionButton", "ContainerButton"];
    const VALUED = ["Slider", "ProgressBar", "SpinBox", "FloatSpinBox"];
    const ANY = undefined; // любой именованный элемент
    UIBlocks.registerModule({
        id: "looks", name: "Элементы", color: "#7a5cf0",
        blocks: [
            { id: "set_text", shape: "cmd", text: "задать текст {C} = {T}",
              args: { C: { type: "control", of: TEXTED }, T: { type: "text", def: "Привет" } },
              gen: (ctx, a) => {
                  if (ctx.typeOf(a.C.raw) === "TextEdit") { ctx.use("rope"); return `${a.C.code}.TextRope = new Rope.Leaf(${a.T.code});`; }
                  return `${a.C.code}.Text = ${a.T.code};`;
              } },
            { id: "set_color", shape: "cmd", text: "цвет текста {C} = {K}",
              args: { C: { type: "control", of: ["Label"] }, K: { type: "color", def: "#31843E" } },
              gen: (ctx, a) => `${a.C.code}.FontColorOverride = Color.FromHex(${a.K.code});` },
            { id: "reset_color", shape: "cmd", text: "сбросить цвет текста {C}",
              args: { C: { type: "control", of: ["Label"] } },
              gen: (ctx, a) => `${a.C.code}.FontColorOverride = null;` },
            { id: "show", shape: "cmd", text: "показать {C}", args: { C: { type: "control", of: ANY } },
              gen: (ctx, a) => `${a.C.code}.Visible = true;` },
            { id: "hide", shape: "cmd", text: "скрыть {C}", args: { C: { type: "control", of: ANY } },
              gen: (ctx, a) => `${a.C.code}.Visible = false;` },
            { id: "toggle_vis", shape: "cmd", text: "переключить видимость {C}", args: { C: { type: "control", of: ANY } },
              gen: (ctx, a) => `${a.C.code}.Visible = !${a.C.code}.Visible;` },
            { id: "enable", shape: "cmd", text: "включить {C}",
              args: { C: { type: "control", of: [...BTN, "LineEdit", "TextEdit"] } },
              gen: (ctx, a) => ["LineEdit", "TextEdit"].includes(ctx.typeOf(a.C.raw)) ? `${a.C.code}.Editable = true;` : `${a.C.code}.Disabled = false;` },
            { id: "disable", shape: "cmd", text: "выключить {C}",
              args: { C: { type: "control", of: [...BTN, "LineEdit", "TextEdit"] } },
              gen: (ctx, a) => ["LineEdit", "TextEdit"].includes(ctx.typeOf(a.C.raw)) ? `${a.C.code}.Editable = false;` : `${a.C.code}.Disabled = true;` },
            { id: "set_pressed", shape: "cmd", text: "отметить {C} = {B}",
              args: { C: { type: "control", of: ["CheckBox", "Button", "ConfirmButton", "TextureButton"] }, B: { type: "bool" } },
              gen: (ctx, a) => `${a.C.code}.Pressed = ${a.B.code};` },
            { id: "set_value", shape: "cmd", text: "значение {C} = {N}",
              args: { C: { type: "control", of: VALUED }, N: { type: "num", def: 50 } },
              gen: (ctx, a) => ctx.typeOf(a.C.raw) === "SpinBox"
                  ? `${a.C.code}.Value = (int) Math.Round(${a.N.code});`
                  : `${a.C.code}.Value = (float) ${a.N.code};` },
            { id: "list_add", shape: "cmd", text: "добавить {T} в {C}",
              args: { T: { type: "text", def: "строка" }, C: { type: "control", of: ["ItemList", "OptionButton"] } },
              gen: (ctx, a) => `${a.C.code}.AddItem(${a.T.code});` },
            { id: "list_clear", shape: "cmd", text: "очистить {C}",
              args: { C: { type: "control", of: ["ItemList", "OptionButton"] } },
              gen: (ctx, a) => `${a.C.code}.Clear();` },
            { id: "set_tab", shape: "cmd", text: "открыть вкладку № {N} в {C}",
              args: { N: { type: "num", def: 1 }, C: { type: "control", of: ["TabContainer"] } },
              gen: (ctx, a) => `${a.C.code}.CurrentTab = Math.Clamp((int) ${a.N.code} - 1, 0, Math.Max(0, ${a.C.code}.ChildCount - 1));` },
            { id: "set_title", shape: "cmd", text: "заголовок окна = {T}", windowOnly: true,
              args: { T: { type: "text", def: "Окно" } },
              gen: (ctx, a) => `Title = ${a.T.code};` },
            { id: "close", shape: "cap", text: "закрыть окно", windowOnly: true,
              gen: () => "Close();" },

            { id: "get_text", shape: "reporter", text: "текст {C}", out: "text",
              args: { C: { type: "control", of: TEXTED } },
              gen: (ctx, a) => {
                  const t = ctx.typeOf(a.C.raw);
                  if (t === "TextEdit") { ctx.use("rope"); return `Rope.Collapse(${a.C.code}.TextRope)`; }
                  if (t === "LineEdit") return `${a.C.code}.Text`;
                  return `${a.C.code}.Text ?? ""`;
              } },
            { id: "get_value", shape: "reporter", text: "значение {C}", out: "num",
              args: { C: { type: "control", of: VALUED } },
              gen: (ctx, a) => `(double) ${a.C.code}.Value` },
            { id: "is_pressed", shape: "bool", text: "{C} отмечена?",
              args: { C: { type: "control", of: ["CheckBox", "Button", "ConfirmButton", "TextureButton"] } },
              gen: (ctx, a) => `${a.C.code}.Pressed` },
            { id: "is_visible", shape: "bool", text: "{C} видим?",
              args: { C: { type: "control", of: ANY } },
              gen: (ctx, a) => `${a.C.code}.Visible` },
            { id: "option_id", shape: "reporter", text: "№ варианта {C}", out: "num",
              args: { C: { type: "control", of: ["OptionButton"] } },
              gen: (ctx, a) => `(double) (${a.C.code}.SelectedId + 1)` },
            { id: "selected_item", shape: "reporter", text: "выбранный элемент {C}", out: "text",
              args: { C: { type: "control", of: ["ItemList"] } },
              gen: (ctx, a) => { ctx.use("linq"); return `${a.C.code}.GetSelected().FirstOrDefault()?.Text ?? ""`; } },
            { id: "item_count", shape: "reporter", text: "кол-во элементов {C}", out: "num",
              args: { C: { type: "control", of: ["ItemList", "OptionButton"] } },
              gen: (ctx, a) => ctx.typeOf(a.C.raw) === "ItemList" ? `(double) ${a.C.code}.Count` : `(double) ${a.C.code}.ItemCount` },
        ],
    });
})();
