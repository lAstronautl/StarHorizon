// Сгенерировано SS14 UI Editor из блоков (вкладка «Логика»).
// Не редактируйте вручную: файл перезаписывается при сохранении. Свой код пишите в .xaml.cs.
#pragma warning disable CS0414 // служебные поля могут не использоваться
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._Horizon.UISamples.UI;

partial class CrewNotesWindow
{
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        NotesEdit.OnTextChanged += _ => UiRun(() =>
        {
            Blk_обновить_счётчик();
        });

        UiRun(() =>
        {
            ThemeOption.AddItem("Тёмная");
            ThemeOption.AddItem("Светлая");
            Blk_обновить_счётчик();
        });

        SaveNotesButton.OnPressed += _ => UiRunAsync(async () =>
        {
            SaveNotesButton.Text = "Сохранено!";
            SaveNotesButton.Disabled = true;
            await Robust.Shared.Timing.Timer.Delay(TimeSpan.FromSeconds(Math.Max(0, 1.5d)));
            if (_uiStopped) return;
            SaveNotesButton.Text = "Сохранить";
            SaveNotesButton.Disabled = false;
        });

        FontSizeSlider.OnValueChanged += _ => UiRun(() =>
        {
            UiLog((string.Concat("Размер шрифта: ", UiText(((double) FontSizeSlider.Value)))));
        });

        OnClose += () =>
        {
            _uiStopped = true;
            _uiCts.Cancel();
        };
    }

    private void Blk_обновить_счётчик()
    {
        CharCountLabel.Text = (string.Concat(UiText(((double) (Rope.Collapse(NotesEdit.TextRope)).Length)), "/1000"));
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
 "vars": [],
 "procs": [
  "обновить счётчик"
 ],
 "scripts": [
  {
   "x": 40,
   "y": 40,
   "blocks": [
    {
     "id": "b573",
     "op": "proc_def",
     "args": {
      "P": "обновить счётчик"
     }
    },
    {
     "id": "b635",
     "op": "set_text",
     "args": {
      "C": "CharCountLabel",
      "T": {
       "id": "b636",
       "op": "op_join",
       "args": {
        "A": {
         "id": "b637",
         "op": "op_length",
         "args": {
          "A": {
           "id": "b638",
           "op": "get_text",
           "args": {
            "C": "NotesEdit"
           }
          }
         }
        },
        "B": "/1000"
       }
      }
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 174,
   "blocks": [
    {
     "id": "b639",
     "op": "ev_text_changed",
     "args": {
      "C": "NotesEdit"
     }
    },
    {
     "id": "b640",
     "op": "proc_call",
     "args": {
      "P": "обновить счётчик"
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 284,
   "blocks": [
    {
     "id": "b641",
     "op": "ev_create",
     "args": {}
    },
    {
     "id": "b642",
     "op": "list_add",
     "args": {
      "T": "Тёмная",
      "C": "ThemeOption"
     }
    },
    {
     "id": "b643",
     "op": "list_add",
     "args": {
      "T": "Светлая",
      "C": "ThemeOption"
     }
    },
    {
     "id": "b644",
     "op": "proc_call",
     "args": {
      "P": "обновить счётчик"
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 459,
   "blocks": [
    {
     "id": "b645",
     "op": "ev_pressed",
     "args": {
      "C": "SaveNotesButton"
     }
    },
    {
     "id": "b646",
     "op": "set_text",
     "args": {
      "C": "SaveNotesButton",
      "T": "Сохранено!"
     }
    },
    {
     "id": "b647",
     "op": "disable",
     "args": {
      "C": "SaveNotesButton"
     }
    },
    {
     "id": "b648",
     "op": "wait",
     "args": {
      "N": "1.5"
     }
    },
    {
     "id": "b649",
     "op": "set_text",
     "args": {
      "C": "SaveNotesButton",
      "T": "Сохранить"
     }
    },
    {
     "id": "b650",
     "op": "enable",
     "args": {
      "C": "SaveNotesButton"
     }
    }
   ]
  },
  {
   "x": 20,
   "y": 715,
   "blocks": [
    {
     "id": "b651",
     "op": "ev_value",
     "args": {
      "C": "FontSizeSlider"
     }
    },
    {
     "id": "b652",
     "op": "log",
     "args": {
      "T": {
       "id": "b653",
       "op": "op_join",
       "args": {
        "A": "Размер шрифта: ",
        "B": {
         "id": "b654",
         "op": "get_value",
         "args": {
          "C": "FontSizeSlider"
         }
        }
       }
      }
     }
    }
   ]
  }
 ]
}
*/
