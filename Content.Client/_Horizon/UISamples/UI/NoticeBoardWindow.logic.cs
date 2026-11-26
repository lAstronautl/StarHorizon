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

partial class NoticeBoardWindow
{
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        PostButton.OnPressed += _ => UiRun(() =>
        {
            ServerSend?.Invoke("опубликовать", (NewNoticeEdit.Text));
            NewNoticeEdit.Text = "";
        });

        UiServerStateChanged += () => UiRun(() =>
        {
            NoticeLabel.Text = (UiServerVar("текст"));
            AuthorLabel.Text = (string.Concat("Автор: ", (UiServerVar("автор"))));
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
     "id": "b1806",
     "op": "ev_pressed",
     "args": {
      "C": "PostButton"
     }
    },
    {
     "id": "b1807",
     "op": "srv_send",
     "args": {
      "A": "опубликовать",
      "V": {
       "id": "b1808",
       "op": "get_text",
       "args": {
        "C": "NewNoticeEdit"
       }
      }
     }
    },
    {
     "id": "b1809",
     "op": "set_text",
     "args": {
      "C": "NewNoticeEdit",
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
     "id": "b1810",
     "op": "srv_update",
     "args": {}
    },
    {
     "id": "b1811",
     "op": "set_text",
     "args": {
      "C": "NoticeLabel",
      "T": {
       "id": "b1812",
       "op": "srv_var",
       "args": {
        "N": "текст"
       }
      }
     }
    },
    {
     "id": "b1813",
     "op": "set_text",
     "args": {
      "C": "AuthorLabel",
      "T": {
       "id": "b1814",
       "op": "op_join",
       "args": {
        "A": "Автор: ",
        "B": {
         "id": "b1815",
         "op": "srv_var",
         "args": {
          "N": "автор"
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
  "id": "HorizonNoticeBoard",
  "name": "доска объявлений",
  "desc": "Планшет с объявлениями. Новое объявление видят все.",
  "parent": "BaseItem",
  "sprite": "Objects/Misc/clipboard.rsi",
  "state": "icon",
  "server": {
   "vars": [
    {
     "name": "текст",
     "type": "text"
    },
    {
     "name": "автор",
     "type": "text"
    },
    {
     "name": "всего",
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
       "id": "b1730",
       "op": "sv_init",
       "args": {}
      },
      {
       "id": "b1731",
       "op": "var_set",
       "args": {
        "V": "текст",
        "X": "Объявлений пока нет"
       }
      }
     ]
    },
    {
     "x": 20,
     "y": 121,
     "blocks": [
      {
       "id": "b1732",
       "op": "sv_action",
       "args": {
        "A": "опубликовать"
       }
      },
      {
       "id": "b1733",
       "op": "ifelse",
       "args": {
        "B": {
         "id": "b1734",
         "op": "op_gt",
         "args": {
          "A": {
           "id": "b1735",
           "op": "op_length",
           "args": {
            "A": {
             "id": "b1736",
             "op": "sv_value",
             "args": {}
            }
           }
          },
          "B": "0"
         }
        }
       },
       "sub": [
        {
         "id": "b1737",
         "op": "var_set",
         "args": {
          "V": "текст",
          "X": {
           "id": "b1738",
           "op": "sv_value",
           "args": {}
          }
         }
        },
        {
         "id": "b1739",
         "op": "var_set",
         "args": {
          "V": "автор",
          "X": {
           "id": "b1740",
           "op": "sv_user_name",
           "args": {}
          }
         }
        },
        {
         "id": "b1741",
         "op": "var_change",
         "args": {
          "V": "всего",
          "N": "1"
         }
        },
        {
         "id": "b1742",
         "op": "sv_popup_all",
         "args": {
          "T": "Новое объявление!"
         }
        }
       ],
       "sub2": [
        {
         "id": "b1743",
         "op": "sv_popup_user",
         "args": {
          "T": "Пустое объявление не опубликовать"
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
