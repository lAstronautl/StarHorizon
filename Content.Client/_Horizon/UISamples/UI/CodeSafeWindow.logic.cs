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

partial class CodeSafeWindow
{
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        OpenButton.OnPressed += _ => UiRun(() =>
        {
            ServerSend?.Invoke("открыть", (CodeEdit.Text));
            CodeEdit.Text = "";
        });

        CodeEdit.OnTextEntered += _ => UiRun(() =>
        {
            ServerSend?.Invoke("открыть", (CodeEdit.Text));
            CodeEdit.Text = "";
        });

        UiServerStateChanged += () => UiRun(() =>
        {
            if ((UiBool(UiServerVar("открыт"))))
            {
                StatusLabel.Text = "ОТКРЫТ";
                StatusLabel.FontColorOverride = Color.FromHex("#31843E");
                OpenButton.Disabled = true;
            }
            else
            {
                StatusLabel.Text = "Закрыт";
            }
            AttemptsLabel.Text = (string.Concat("Ошибок: ", UiText((UiNum(UiServerVar("ошибок"))))));
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
     "id": "b1260",
     "op": "ev_pressed",
     "args": {
      "C": "OpenButton"
     }
    },
    {
     "id": "b1261",
     "op": "srv_send",
     "args": {
      "A": "открыть",
      "V": {
       "id": "b1262",
       "op": "get_text",
       "args": {
        "C": "CodeEdit"
       }
      }
     }
    },
    {
     "id": "b1263",
     "op": "set_text",
     "args": {
      "C": "CodeEdit",
      "T": ""
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 175,
   "blocks": [
    {
     "id": "b1264",
     "op": "ev_text_entered",
     "args": {
      "C": "CodeEdit"
     }
    },
    {
     "id": "b1265",
     "op": "srv_send",
     "args": {
      "A": "открыть",
      "V": {
       "id": "b1266",
       "op": "get_text",
       "args": {
        "C": "CodeEdit"
       }
      }
     }
    },
    {
     "id": "b1267",
     "op": "set_text",
     "args": {
      "C": "CodeEdit",
      "T": ""
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 330,
   "blocks": [
    {
     "id": "b1268",
     "op": "srv_update",
     "args": {}
    },
    {
     "id": "b1269",
     "op": "ifelse",
     "args": {
      "B": {
       "id": "b1270",
       "op": "srv_var",
       "args": {
        "N": "открыт"
       }
      }
     },
     "sub": [
      {
       "id": "b1271",
       "op": "set_text",
       "args": {
        "C": "StatusLabel",
        "T": "ОТКРЫТ"
       }
      },
      {
       "id": "b1272",
       "op": "set_color",
       "args": {
        "C": "StatusLabel",
        "K": "#31843E"
       }
      },
      {
       "id": "b1273",
       "op": "disable",
       "args": {
        "C": "OpenButton"
       }
      }
     ],
     "sub2": [
      {
       "id": "b1274",
       "op": "set_text",
       "args": {
        "C": "StatusLabel",
        "T": "Закрыт"
       }
      }
     ]
    },
    {
     "id": "b1275",
     "op": "set_text",
     "args": {
      "C": "AttemptsLabel",
      "T": {
       "id": "b1276",
       "op": "op_join",
       "args": {
        "A": "Ошибок: ",
        "B": {
         "id": "b1277",
         "op": "srv_var",
         "args": {
          "N": "ошибок"
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
  "id": "HorizonCodeSafe",
  "name": "кодовый сейф",
  "desc": "Маленький сейф с кодовым замком. После трёх ошибок блокируется.",
  "parent": "BaseItem",
  "sprite": "Objects/Devices/forensic_scanner.rsi",
  "state": "icon",
  "server": {
   "vars": [
    {
     "name": "код",
     "type": "text"
    },
    {
     "name": "ошибок",
     "type": "num"
    },
    {
     "name": "открыт",
     "type": "bool"
    }
   ],
   "procs": [],
   "scripts": [
    {
     "x": 20,
     "y": 20,
     "blocks": [
      {
       "id": "b1178",
       "op": "sv_init",
       "args": {}
      },
      {
       "id": "b1179",
       "op": "var_set",
       "args": {
        "V": "код",
        "X": "1234"
       }
      }
     ]
    },
    {
     "x": 20,
     "y": 121,
     "blocks": [
      {
       "id": "b1180",
       "op": "sv_action",
       "args": {
        "A": "открыть"
       }
      },
      {
       "id": "b1181",
       "op": "ifelse",
       "args": {
        "B": {
         "id": "b1182",
         "op": "var_get",
         "args": {
          "V": "открыт"
         }
        }
       },
       "sub": [
        {
         "id": "b1183",
         "op": "sv_popup_user",
         "args": {
          "T": "Сейф уже открыт"
         }
        }
       ],
       "sub2": [
        {
         "id": "b1184",
         "op": "ifelse",
         "args": {
          "B": {
           "id": "b1185",
           "op": "op_gt",
           "args": {
            "A": {
             "id": "b1186",
             "op": "var_get",
             "args": {
              "V": "ошибок"
             }
            },
            "B": "2"
           }
          }
         },
         "sub": [
          {
           "id": "b1187",
           "op": "sv_popup_user",
           "args": {
            "T": "Сейф заблокирован"
           }
          }
         ],
         "sub2": [
          {
           "id": "b1188",
           "op": "ifelse",
           "args": {
            "B": {
             "id": "b1189",
             "op": "op_eq",
             "args": {
              "A": {
               "id": "b1190",
               "op": "sv_value",
               "args": {}
              },
              "B": {
               "id": "b1191",
               "op": "var_get",
               "args": {
                "V": "код"
               }
              }
             }
            }
           },
           "sub": [
            {
             "id": "b1192",
             "op": "var_set",
             "args": {
              "V": "открыт",
              "X": "true"
             }
            },
            {
             "id": "b1193",
             "op": "sv_give",
             "args": {
              "P": "SpaceCash100"
             }
            },
            {
             "id": "b1194",
             "op": "sv_popup_user",
             "args": {
              "T": "Сейф открыт!"
             }
            },
            {
             "id": "b1195",
             "op": "sv_sound",
             "args": {
              "P": "/Audio/Machines/ding.ogg"
             }
            }
           ],
           "sub2": [
            {
             "id": "b1196",
             "op": "var_change",
             "args": {
              "V": "ошибок",
              "N": "1"
             }
            },
            {
             "id": "b1197",
             "op": "sv_popup_user",
             "args": {
              "T": "Неверный код"
             }
            }
           ]
          }
         ]
        }
       ]
      }
     ]
    }
   ]
  }
 }
}
*/
