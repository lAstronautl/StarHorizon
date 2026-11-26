// Сгенерировано SS14 UI Editor (предмет «кодовый сейф»). Не редактируйте вручную: файл перезаписывается при сохранении.
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

/// <summary>Серверная логика предмета «кодовый сейф», собранная из блоков.</summary>
public sealed class HorizonCodeSafeItemSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HorizonCodeSafeItemComponent, MapInitEvent>(OnMapInit);
        Subs.BuiEvents<HorizonCodeSafeItemComponent>(HorizonCodeSafeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<HorizonCodeSafeUiMessage>(OnMessage);
        });
    }

    private void OnMapInit(Entity<HorizonCodeSafeItemComponent> ent, ref MapInitEvent args)
    {
        var user = EntityUid.Invalid;
        var value = "";
        Script1(ent, user, value);
    }

    private void OnOpened(Entity<HorizonCodeSafeItemComponent> ent, ref BoundUIOpenedEvent args)
    {
        var user = args.Actor;
        var value = "";

        UpdateUi(ent);
    }

    private void OnMessage(Entity<HorizonCodeSafeItemComponent> ent, ref HorizonCodeSafeUiMessage msg)
    {
        var user = msg.Actor;
        var value = msg.Value;
        if (msg.Action == "открыть")
            Script2(ent, user, value);
        UpdateUi(ent);
    }

    /// <summary>Отправляет окну текущие переменные предмета.</summary>
    public void UpdateUi(Entity<HorizonCodeSafeItemComponent> ent)
    {
        var vars = new Dictionary<string, string>
        {
            ["код"] = ent.Comp.V_kod,
            ["ошибок"] = UiText(ent.Comp.V_oshibok),
            ["открыт"] = UiText(ent.Comp.V_otkryt),
        };
        _ui.SetUiState(ent.Owner, HorizonCodeSafeUiKey.Key, new HorizonCodeSafeUiState(vars));
    }

    // когда предмет появился
    private void Script1(Entity<HorizonCodeSafeItemComponent> ent, EntityUid user, string value)
    {
        ent.Comp.V_kod = UiText(1234d);
    }

    // когда пришло действие "открыть"
    private void Script2(Entity<HorizonCodeSafeItemComponent> ent, EntityUid user, string value)
    {
        if ((ent.Comp.V_otkryt))
        {
            if (user.Valid)
                _popup.PopupEntity("Сейф уже открыт", ent, user);
        }
        else
        {
            if (((ent.Comp.V_oshibok) > 2d))
            {
                if (user.Valid)
                    _popup.PopupEntity("Сейф заблокирован", ent, user);
            }
            else
            {
                if ((UiEq((value), (ent.Comp.V_kod))))
                {
                    ent.Comp.V_otkryt = UiBool("true");
                    _hands.PickupOrDrop(user.Valid ? user : null, Spawn("SpaceCash100", Transform(ent).Coordinates));
                    if (user.Valid)
                        _popup.PopupEntity("Сейф открыт!", ent, user);
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/ding.ogg"), ent);
                }
                else
                {
                    ent.Comp.V_oshibok += 1d;
                    if (user.Valid)
                        _popup.PopupEntity("Неверный код", ent, user);
                }
            }
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
