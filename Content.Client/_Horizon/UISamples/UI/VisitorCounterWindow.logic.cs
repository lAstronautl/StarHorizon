// Сгенерировано SS14 UI Editor из блоков (вкладка «Логика»).
// Не редактируйте вручную: файл перезаписывается при сохранении. Свой код пишите в .xaml.cs.
#pragma warning disable CS0414, CS0067 // служебные поля/события могут не использоваться
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._Horizon.UISamples.UI;

partial class VisitorCounterWindow
{
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        PlusButton.OnPressed += _ => UiRun(() =>
        {
            ServerSend?.Invoke("плюс", "");
        });

        ResetButton.OnPressed += _ => UiRun(() =>
        {
            ServerSend?.Invoke("сброс", "");
        });

        UiServerStateChanged += () => UiRun(() =>
        {
            CountLabel.Text = UiText((UiNum(UiServerVar("счёт"))));
            LastLabel.Text = (string.Concat("Последний: ", (UiServerVar("последний"))));
        });

        OnClose += () =>
        {
            _uiStopped = true;
            _uiCts.Cancel();
        };
    }

    // --- связь с сервером ---

    /// <summary>Отправка действия на сервер. Назначается BoundUserInterface предмета.</summary>
    public Action<string, string>? ServerSend;

    private readonly Dictionary<string, string> _uiServerVars = new();

    private event Action? UiServerStateChanged;

    /// <summary>Новое состояние от сервера (переменные предмета).</summary>
    public void ApplyServerState(Dictionary<string, string> vars)
    {
        _uiServerVars.Clear();
        foreach (var (k, v) in vars)
            _uiServerVars[k] = v;
        UiServerStateChanged?.Invoke();
    }

    private string UiServerVar(string name) => _uiServerVars.TryGetValue(name, out var v) ? v : "";

    // --- вспомогательные методы ---

    private void UiRun(Action action)
    {
        try { action(); }
        catch (Exception e) { UiLogError(e); }
    }

    private async void UiRunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { UiLogError(e); }
    }

    private static Robust.Shared.Log.ISawmill UiSawmill =>
        Robust.Shared.IoC.IoCManager.Resolve<Robust.Shared.Log.ILogManager>().GetSawmill("ui.blocks");

    private static void UiLogError(Exception e) => UiSawmill.Error(e.ToString());

    private static void UiLog(string text) => UiSawmill.Info(text);

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
}

/* ui-blocks
{
 "vars": [],
 "procs": [],
 "scripts": [
  {
   "x": 20,
   "y": 20,
   "blocks": [
    {
     "id": "b715",
     "op": "ev_pressed",
     "args": {
      "C": "PlusButton"
     }
    },
    {
     "id": "b716",
     "op": "srv_send",
     "args": {
      "A": "плюс",
      "V": ""
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 128,
   "blocks": [
    {
     "id": "b717",
     "op": "ev_pressed",
     "args": {
      "C": "ResetButton"
     }
    },
    {
     "id": "b718",
     "op": "srv_send",
     "args": {
      "A": "сброс",
      "V": ""
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 236,
   "blocks": [
    {
     "id": "b719",
     "op": "srv_update",
     "args": {}
    },
    {
     "id": "b720",
     "op": "set_text",
     "args": {
      "C": "CountLabel",
      "T": {
       "id": "b721",
       "op": "srv_var",
       "args": {
        "N": "счёт"
       }
      }
     }
    },
    {
     "id": "b722",
     "op": "set_text",
     "args": {
      "C": "LastLabel",
      "T": {
       "id": "b723",
       "op": "op_join",
       "args": {
        "A": "Последний: ",
        "B": {
         "id": "b724",
         "op": "srv_var",
         "args": {
          "N": "последний"
         }
        }
       }
      }
     }
    }
   ]
  }
 ],
 "item": {
  "id": "HorizonVisitorCounter",
  "name": "счётчик посетителей",
  "desc": "Отметьтесь, что были здесь. Счёт общий для всех.",
  "parent": "BaseItem",
  "sprite": "Objects/Devices/signaller.rsi",
  "state": "icon",
  "server": {
   "vars": [
    {
     "name": "счёт",
     "type": "num"
    },
    {
     "name": "последний",
     "type": "text"
    }
   ],
   "procs": [],
   "scripts": [
    {
     "x": 20,
     "y": 20,
     "blocks": [
      {
       "id": "b642",
       "op": "sv_action",
       "args": {
        "A": "плюс"
       }
      },
      {
       "id": "b643",
       "op": "var_change",
       "args": {
        "V": "счёт",
        "N": "1"
       }
      },
      {
       "id": "b644",
       "op": "var_set",
       "args": {
        "V": "последний",
        "X": {
         "id": "b645",
         "op": "sv_user_name",
         "args": {}
        }
       }
      },
      {
       "id": "b646",
       "op": "sv_popup_all",
       "args": {
        "T": {
         "id": "b647",
         "op": "op_join",
         "args": {
          "A": {
           "id": "b648",
           "op": "sv_user_name",
           "args": {}
          },
          "B": " отметился"
         }
        }
       }
      }
     ]
    },
    {
     "x": 20,
     "y": 216,
     "blocks": [
      {
       "id": "b649",
       "op": "sv_action",
       "args": {
        "A": "сброс"
       }
      },
      {
       "id": "b650",
       "op": "var_set",
       "args": {
        "V": "счёт",
        "X": "0"
       }
      },
      {
       "id": "b651",
       "op": "var_set",
       "args": {
        "V": "последний",
        "X": "Пока никого"
       }
      },
      {
       "id": "b652",
       "op": "sv_sound",
       "args": {
        "P": "/Audio/Machines/beep.ogg"
       }
      }
     ]
    }
   ]
  }
 }
}
*/
