// Сгенерировано SS14 UI Editor из блоков (вкладка «Логика»).
// Не редактируйте вручную: файл перезаписывается при сохранении. Свой код пишите в .xaml.cs.
#pragma warning disable CS0414 // служебные поля могут не использоваться
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._Horizon.UISamples.UI;

partial class ShuttleStatusWindow
{
    private double _v_скорость;
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        EngineButton.OnToggled += _ => UiRun(() =>
        {
            if ((EngineButton.Pressed))
            {
                StatusLabel.Text = "Двигатели запущены";
                StatusLabel.FontColorOverride = Color.FromHex("#31843E");
            }
            else
            {
                StatusLabel.Text = "Двигатели остановлены";
                StatusLabel.FontColorOverride = Color.FromHex("#A5762F");
            }
        });

        Robust.Shared.Timing.Timer.SpawnRepeating(TimeSpan.FromSeconds(Math.Max(0.05, 0.5d)), () => UiRun(() =>
        {
            if ((EngineButton.Pressed))
            {
                _v_скорость += 1d;
                FuelBar.Value = (float) (((double) FuelBar.Value) - 0.01d);
            }
            SpeedLabel.Text = (string.Concat(UiText((_v_скорость)), " м/с"));
        }), _uiCts.Token);

        EmergencyStopButton.OnPressed += _ => UiRunAsync(async () =>
        {
            EngineButton.Pressed = false;
            _v_скорость = 0d;
            StatusLabel.Text = "АВАРИЙНАЯ ОСТАНОВКА";
            StatusLabel.FontColorOverride = Color.FromHex("#BB3232");
            await Robust.Shared.Timing.Timer.Delay(TimeSpan.FromSeconds(Math.Max(0, 2d)));
            if (_uiStopped) return;
            StatusLabel.Text = "В норме";
            StatusLabel.FontColorOverride = null;
        });

        OnClose += () =>
        {
            _uiStopped = true;
            _uiCts.Cancel();
        };
    }

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
 "vars": [
  {
   "name": "скорость",
   "type": "num"
  }
 ],
 "procs": [],
 "scripts": [
  {
   "x": 20,
   "y": 20,
   "blocks": [
    {
     "id": "b296",
     "op": "ev_toggled",
     "args": {
      "C": "EngineButton"
     }
    },
    {
     "id": "b297",
     "op": "ifelse",
     "args": {
      "B": {
       "id": "b298",
       "op": "is_pressed",
       "args": {
        "C": "EngineButton"
       }
      }
     },
     "sub": [
      {
       "id": "b299",
       "op": "set_text",
       "args": {
        "C": "StatusLabel",
        "T": "Двигатели запущены"
       }
      },
      {
       "id": "b300",
       "op": "set_color",
       "args": {
        "C": "StatusLabel",
        "K": "#31843E"
       }
      }
     ],
     "sub2": [
      {
       "id": "b301",
       "op": "set_text",
       "args": {
        "C": "StatusLabel",
        "T": "Двигатели остановлены"
       }
      },
      {
       "id": "b302",
       "op": "set_color",
       "args": {
        "C": "StatusLabel",
        "K": "#A5762F"
       }
      }
     ]
    }
   ]
  },
  {
   "x": 20,
   "y": 348,
   "blocks": [
    {
     "id": "b303",
     "op": "ev_timer",
     "args": {
      "N": "0.5"
     }
    },
    {
     "id": "b304",
     "op": "if",
     "args": {
      "B": {
       "id": "b305",
       "op": "is_pressed",
       "args": {
        "C": "EngineButton"
       }
      }
     },
     "sub": [
      {
       "id": "b306",
       "op": "var_change",
       "args": {
        "V": "скорость",
        "N": "1"
       }
      },
      {
       "id": "b307",
       "op": "set_value",
       "args": {
        "C": "FuelBar",
        "N": {
         "id": "b308",
         "op": "op_sub",
         "args": {
          "A": {
           "id": "b309",
           "op": "get_value",
           "args": {
            "C": "FuelBar"
           }
          },
          "B": "0.01"
         }
        }
       }
      }
     ]
    },
    {
     "id": "b310",
     "op": "set_text",
     "args": {
      "C": "SpeedLabel",
      "T": {
       "id": "b311",
       "op": "op_join",
       "args": {
        "A": {
         "id": "b312",
         "op": "var_get",
         "args": {
          "V": "скорость"
         }
        },
        "B": " м/с"
       }
      }
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 629,
   "blocks": [
    {
     "id": "b313",
     "op": "ev_pressed",
     "args": {
      "C": "EmergencyStopButton"
     }
    },
    {
     "id": "b314",
     "op": "set_pressed",
     "args": {
      "C": "EngineButton"
     }
    },
    {
     "id": "b315",
     "op": "var_set",
     "args": {
      "V": "скорость",
      "X": "0"
     }
    },
    {
     "id": "b316",
     "op": "set_text",
     "args": {
      "C": "StatusLabel",
      "T": "АВАРИЙНАЯ ОСТАНОВКА"
     }
    },
    {
     "id": "b317",
     "op": "set_color",
     "args": {
      "C": "StatusLabel",
      "K": "#BB3232"
     }
    },
    {
     "id": "b318",
     "op": "wait",
     "args": {
      "N": "2"
     }
    },
    {
     "id": "b319",
     "op": "set_text",
     "args": {
      "C": "StatusLabel",
      "T": "В норме"
     }
    },
    {
     "id": "b320",
     "op": "reset_color",
     "args": {
      "C": "StatusLabel"
     }
    }
   ]
  }
 ]
}
*/
