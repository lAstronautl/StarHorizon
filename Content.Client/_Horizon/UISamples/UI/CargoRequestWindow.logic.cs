// Сгенерировано SS14 UI Editor из блоков (вкладка «Логика»).
// Не редактируйте вручную: файл перезаписывается при сохранении. Свой код пишите в .xaml.cs.
#pragma warning disable CS0414, CS0067 // служебные поля/события могут не использоваться
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._Horizon.UISamples.UI;

partial class CargoRequestWindow
{
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        UiRun(() =>
        {
            ProductList.AddItem("Ящик инструментов");
            ProductList.AddItem("Медицинский набор");
            ProductList.AddItem("Генератор П.А.С.Е.Р.");
        });

        ProductList.OnItemSelected += _ => UiRun(() =>
        {
            ProductName.Text = (ProductList.GetSelected().FirstOrDefault()?.Text ?? "");
        });

        OrderButton.OnPressed += _ => UiRun(() =>
        {
            if ((UiEq((ReasonEdit.Text), "")))
            {
                BalanceLabel.Text = "Укажите причину заказа";
            }
            else
            {
                BalanceLabel.Text = (string.Concat("Заказано: ", (ProductList.GetSelected().FirstOrDefault()?.Text ?? "")));
                UiLog("отладка");
                ServerSend?.Invoke("заказать", (ProductList.GetSelected().FirstOrDefault()?.Text ?? ""));
            }
        });

        UiServerStateChanged += () => UiRun(() =>
        {
            BalanceLabel.Text = (string.Concat("Баланс: ", UiText((UiNum(UiServerVar("баланс"))))));
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
     "id": "b439",
     "op": "ev_create",
     "args": {}
    },
    {
     "id": "b440",
     "op": "list_add",
     "args": {
      "T": "Ящик инструментов",
      "C": "ProductList"
     }
    },
    {
     "id": "b441",
     "op": "list_add",
     "args": {
      "T": "Медицинский набор",
      "C": "ProductList"
     }
    },
    {
     "id": "b442",
     "op": "list_add",
     "args": {
      "T": "Генератор П.А.С.Е.Р.",
      "C": "ProductList"
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 195,
   "blocks": [
    {
     "id": "b443",
     "op": "ev_item",
     "args": {
      "C": "ProductList"
     }
    },
    {
     "id": "b444",
     "op": "set_text",
     "args": {
      "C": "ProductName",
      "T": {
       "id": "b445",
       "op": "selected_item",
       "args": {
        "C": "ProductList"
       }
      }
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 313,
   "blocks": [
    {
     "id": "b446",
     "op": "ev_pressed",
     "args": {
      "C": "OrderButton"
     }
    },
    {
     "id": "b447",
     "op": "ifelse",
     "args": {
      "B": {
       "id": "b448",
       "op": "op_eq",
       "args": {
        "A": {
         "id": "b449",
         "op": "get_text",
         "args": {
          "C": "ReasonEdit"
         }
        },
        "B": ""
       }
      }
     },
     "sub": [
      {
       "id": "b450",
       "op": "set_text",
       "args": {
        "C": "BalanceLabel",
        "T": "Укажите причину заказа"
       }
      }
     ],
     "sub2": [
      {
       "id": "b451",
       "op": "set_text",
       "args": {
        "C": "BalanceLabel",
        "T": {
         "id": "b452",
         "op": "op_join",
         "args": {
          "A": "Заказано: ",
          "B": {
           "id": "b453",
           "op": "selected_item",
           "args": {
            "C": "ProductList"
           }
          }
         }
        }
       }
      },
      {
       "id": "b454",
       "op": "log",
       "args": {
        "T": "отладка"
       }
      },
      {
       "id": "b793",
       "op": "srv_send",
       "args": {
        "A": "заказать",
        "V": {
         "id": "b794",
         "op": "selected_item",
         "args": {
          "C": "ProductList"
         }
        }
       }
      }
     ]
    }
   ]
  },
  {
   "x": 20,
   "y": 671,
   "blocks": [
    {
     "id": "b795",
     "op": "srv_update",
     "args": {}
    },
    {
     "id": "b796",
     "op": "set_text",
     "args": {
      "C": "BalanceLabel",
      "T": {
       "id": "b797",
       "op": "op_join",
       "args": {
        "A": "Баланс: ",
        "B": {
         "id": "b798",
         "op": "srv_var",
         "args": {
          "N": "баланс"
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
  "id": "HorizonCargoTerminal",
  "name": "грузовой терминал",
  "desc": "Карманный терминал заказа грузов. Собран в UI Editor.",
  "parent": "BaseItem",
  "sprite": "Objects/Devices/gps.rsi",
  "state": "icon",
  "server": {
   "vars": [
    {
     "name": "баланс",
     "type": "num"
    },
    {
     "name": "заказов",
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
       "id": "b717",
       "op": "sv_init",
       "args": {}
      },
      {
       "id": "b718",
       "op": "var_set",
       "args": {
        "V": "баланс",
        "X": "100"
       }
      }
     ]
    },
    {
     "x": 20,
     "y": 121,
     "blocks": [
      {
       "id": "b719",
       "op": "sv_action",
       "args": {
        "A": "заказать"
       }
      },
      {
       "id": "b720",
       "op": "ifelse",
       "args": {
        "B": {
         "id": "b721",
         "op": "op_gt",
         "args": {
          "A": {
           "id": "b722",
           "op": "var_get",
           "args": {
            "V": "баланс"
           }
          },
          "B": "9"
         }
        }
       },
       "sub": [
        {
         "id": "b723",
         "op": "var_change",
         "args": {
          "V": "баланс",
          "N": "-10"
         }
        },
        {
         "id": "b724",
         "op": "var_change",
         "args": {
          "V": "заказов",
          "N": "1"
         }
        },
        {
         "id": "b725",
         "op": "sv_give",
         "args": {
          "P": "FoodApple"
         }
        },
        {
         "id": "b726",
         "op": "sv_popup_user",
         "args": {
          "T": {
           "id": "b727",
           "op": "op_join",
           "args": {
            "A": "Заказано: ",
            "B": {
             "id": "b728",
             "op": "sv_value",
             "args": {}
            }
           }
          }
         }
        },
        {
         "id": "b729",
         "op": "sv_sound",
         "args": {
          "P": "/Audio/Machines/ding.ogg"
         }
        }
       ],
       "sub2": [
        {
         "id": "b730",
         "op": "sv_popup_user",
         "args": {
          "T": "Недостаточно средств"
         }
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
