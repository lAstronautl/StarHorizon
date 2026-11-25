// Сгенерировано SS14 UI Editor (предмет «грузовой терминал»). Не редактируйте вручную: файл перезаписывается при сохранении.
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

/// <summary>Серверная логика предмета «грузовой терминал», собранная из блоков.</summary>
public sealed class HorizonCargoTerminalItemSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HorizonCargoTerminalItemComponent, MapInitEvent>(OnMapInit);
        Subs.BuiEvents<HorizonCargoTerminalItemComponent>(HorizonCargoTerminalUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<HorizonCargoTerminalUiMessage>(OnMessage);
        });
    }

    private void OnMapInit(Entity<HorizonCargoTerminalItemComponent> ent, ref MapInitEvent args)
    {
        var user = EntityUid.Invalid;
        var value = "";
        Script1(ent, user, value);
    }

    private void OnOpened(Entity<HorizonCargoTerminalItemComponent> ent, ref BoundUIOpenedEvent args)
    {
        var user = args.Actor;
        var value = "";

        UpdateUi(ent);
    }

    private void OnMessage(Entity<HorizonCargoTerminalItemComponent> ent, ref HorizonCargoTerminalUiMessage msg)
    {
        var user = msg.Actor;
        var value = msg.Value;
        if (msg.Action == "заказать")
            Script2(ent, user, value);
        UpdateUi(ent);
    }

    /// <summary>Отправляет окну текущие переменные предмета.</summary>
    public void UpdateUi(Entity<HorizonCargoTerminalItemComponent> ent)
    {
        var vars = new Dictionary<string, string>
        {
            ["баланс"] = UiText(ent.Comp.V_balans),
            ["заказов"] = UiText(ent.Comp.V_zakazov),
        };
        _ui.SetUiState(ent.Owner, HorizonCargoTerminalUiKey.Key, new HorizonCargoTerminalUiState(vars));
    }

    // когда предмет появился
    private void Script1(Entity<HorizonCargoTerminalItemComponent> ent, EntityUid user, string value)
    {
        ent.Comp.V_balans = 100d;
    }

    // когда пришло действие "заказать"
    private void Script2(Entity<HorizonCargoTerminalItemComponent> ent, EntityUid user, string value)
    {
        if (((ent.Comp.V_balans) > 9d))
        {
            ent.Comp.V_balans += -10d;
            ent.Comp.V_zakazov += 1d;
            _hands.PickupOrDrop(user.Valid ? user : null, Spawn("FoodApple", Transform(ent).Coordinates));
            if (user.Valid)
                _popup.PopupEntity((string.Concat("Заказано: ", (value))), ent, user);
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/ding.ogg"), ent);
        }
        else
        {
            if (user.Valid)
                _popup.PopupEntity("Недостаточно средств", ent, user);
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
