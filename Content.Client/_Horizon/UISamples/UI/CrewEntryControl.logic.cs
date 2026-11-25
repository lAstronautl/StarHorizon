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

partial class CrewEntryControl
{
    private double _v_счётчик;
    private bool _uiStopped;
    private readonly CancellationTokenSource _uiCts = new();

    private void InitializeLogic()
    {
        SelectButton.OnPressed += _ => UiRun(() =>
        {
            _v_счётчик += 1d;
            JobLabel.Text = (string.Concat("Выбрано раз: ", UiText((_v_счётчик))));
            JobIcon.Visible = true;
        });
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
   "name": "счётчик",
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
     "id": "b340",
     "op": "ev_pressed",
     "args": {
      "C": "SelectButton"
     },
     "nx": 40,
     "ny": 40
    },
    {
     "id": "b341",
     "op": "var_change",
     "args": {
      "V": "счётчик",
      "N": "1"
     },
     "nx": 330,
     "ny": 40
    },
    {
     "id": "b342",
     "op": "set_text",
     "args": {
      "C": "JobLabel",
      "T": {
       "id": "b343",
       "op": "op_join",
       "args": {
        "A": "Выбрано раз: ",
        "B": {
         "id": "b344",
         "op": "var_get",
         "args": {
          "V": "счётчик"
         },
         "nx": 620,
         "ny": 70
        }
       },
       "nx": 890,
       "ny": 70
      }
     },
     "nx": 1160,
     "ny": 40
    },
    {
     "id": "b345",
     "op": "show",
     "args": {
      "C": "JobIcon"
     },
     "nx": 1450,
     "ny": 40
    }
   ]
  }
 ]
}
*/
