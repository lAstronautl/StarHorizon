// Сгенерировано SS14 UI Editor (предмет «доска объявлений»). Не редактируйте вручную: файл перезаписывается при сохранении.
#pragma warning disable CS0414
using System;
using System.Collections.Generic;
using System.Globalization;
using Content.Shared._Horizon.UIItems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Horizon.UIItems;

/// <summary>Серверная логика предмета «доска объявлений», собранная из блоков.</summary>
public sealed class HorizonNoticeBoardItemSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HorizonNoticeBoardItemComponent, MapInitEvent>(OnMapInit);
        Subs.BuiEvents<HorizonNoticeBoardItemComponent>(HorizonNoticeBoardUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<HorizonNoticeBoardUiMessage>(OnMessage);
        });
    }

    private void OnMapInit(Entity<HorizonNoticeBoardItemComponent> ent, ref MapInitEvent args)
    {
        var user = EntityUid.Invalid;
        var value = "";
        Script1(ent, user, value);
    }

    private void OnOpened(Entity<HorizonNoticeBoardItemComponent> ent, ref BoundUIOpenedEvent args)
    {
        var user = args.Actor;
        var value = "";

        UpdateUi(ent);
    }

    private void OnMessage(Entity<HorizonNoticeBoardItemComponent> ent, ref HorizonNoticeBoardUiMessage msg)
    {
        var user = msg.Actor;
        var value = msg.Value;
        if (msg.Action == "опубликовать")
            Script2(ent, user, value);
        UpdateUi(ent);
    }

    /// <summary>Отправляет окну текущие переменные предмета.</summary>
    public void UpdateUi(Entity<HorizonNoticeBoardItemComponent> ent)
    {
        var vars = new Dictionary<string, string>
        {
            ["текст"] = ent.Comp.V_tekst,
            ["автор"] = ent.Comp.V_avtor,
            ["всего"] = UiText(ent.Comp.V_vsego),
        };
        _ui.SetUiState(ent.Owner, HorizonNoticeBoardUiKey.Key, new HorizonNoticeBoardUiState(vars));
    }

    // когда предмет появился
    private void Script1(Entity<HorizonNoticeBoardItemComponent> ent, EntityUid user, string value)
    {
        ent.Comp.V_tekst = "Объявлений пока нет";
    }

    // когда пришло действие "опубликовать"
    private void Script2(Entity<HorizonNoticeBoardItemComponent> ent, EntityUid user, string value)
    {
        if ((((double) (value).Length) > 0d))
        {
            ent.Comp.V_tekst = (value);
            ent.Comp.V_avtor = (user.Valid ? Name(user) : "");
            ent.Comp.V_vsego += 1d;
            _popup.PopupEntity("Новое объявление!", ent);
        }
        else
        {
            if (user.Valid)
                _popup.PopupEntity("Пустое объявление не опубликовать", ent, user);
        }
    }

    // --- вспомогательные методы ---

    private static double UiNum(string? s) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string UiText(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string UiText(bool v) => v ? "true" : "false";

    private static bool UiBool(string s) => s.Trim().ToLowerInvariant() is "true" or "1" or "да";

    private static bool UiEq(string a, string b)
    {
        if (double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(b.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return Math.Abs(x - y) < 1e-9;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private void UiLog(string text) => Log.Info(text);
}
