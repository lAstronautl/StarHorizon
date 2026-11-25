// Модуль «События»: блоки-шапки, с которых начинается скрипт.
"use strict";
(() => {
    const BUTTONS = ["Button", "ConfirmButton", "TextureButton", "ContainerButton", "CheckBox", "OptionButton"];
    UIBlocks.registerModule({
        id: "events", name: "События", color: "#d9a400",
        blocks: [
            { id: "ev_create", shape: "hat", text: "когда окно создано",
              help: "Выполняется один раз, при создании окна",
              gen: (ctx, a, run) => run + ";" },
            { id: "ev_pressed", shape: "hat", text: "когда нажата {C}",
              args: { C: { type: "control", of: BUTTONS } },
              gen: (ctx, a, run) => `${a.C.code}.OnPressed += _ => ${run};` },
            { id: "ev_toggled", shape: "hat", text: "когда переключена {C}",
              help: "Для CheckBox и кнопок с ToggleMode",
              args: { C: { type: "control", of: ["Button", "CheckBox", "TextureButton", "ConfirmButton"] } },
              gen: (ctx, a, run) => `${a.C.code}.OnToggled += _ => ${run};` },
            { id: "ev_text_entered", shape: "hat", text: "когда в {C} нажат Enter",
              args: { C: { type: "control", of: ["LineEdit"] } },
              gen: (ctx, a, run) => `${a.C.code}.OnTextEntered += _ => ${run};` },
            { id: "ev_text_changed", shape: "hat", text: "когда меняется текст в {C}",
              args: { C: { type: "control", of: ["LineEdit", "TextEdit"] } },
              gen: (ctx, a, run) => `${a.C.code}.OnTextChanged += _ => ${run};` },
            { id: "ev_option", shape: "hat", text: "когда выбран вариант в {C}",
              args: { C: { type: "control", of: ["OptionButton"] } },
              gen: (ctx, a, run) => `${a.C.code}.OnItemSelected += args =>\n{\n    ${a.C.code}.SelectId(args.Id);\n${run.split("\n").map(l => "    " + l).join("\n")};\n};` },
            { id: "ev_item", shape: "hat", text: "когда выбран элемент в {C}",
              args: { C: { type: "control", of: ["ItemList"] } },
              gen: (ctx, a, run) => `${a.C.code}.OnItemSelected += _ => ${run};` },
            { id: "ev_value", shape: "hat", text: "когда меняется значение {C}",
              args: { C: { type: "control", of: ["Slider", "SpinBox", "FloatSpinBox"] } },
              gen: (ctx, a, run) => {
                  const ev = ctx.typeOf(a.C.raw) === "SpinBox" ? "ValueChanged" : "OnValueChanged";
                  return `${a.C.code}.${ev} += _ => ${run};`;
              } },
            { id: "ev_timer", shape: "hat", text: "каждые {N} сек.",
              help: "Повторяется, пока окно открыто",
              args: { N: { type: "num", def: 1 } },
              gen: (ctx, a, run) => `Robust.Shared.Timing.Timer.SpawnRepeating(TimeSpan.FromSeconds(Math.Max(0.05, ${a.N.code})), () => ${run}, _uiCts.Token);` },
            { id: "ev_close", shape: "hat", text: "когда окно закрывается", windowOnly: true,
              gen: (ctx, a, run) => `OnClose += () => ${run};` },
        ],
    });
})();
