using Content.Client._Horizon.UISamples.UI;
using Content.Client.UserInterface.Controls;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Client._Horizon.UISamples;

/// <summary>
/// Открывает образцы интерфейсов, собранные в Tools/_Horizon/UIEditor, чтобы посмотреть их в игре.
/// </summary>
[UsedImplicitly]
public sealed class UISampleCommand : LocalizedCommands
{
    public override string Command => "ui_sample";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : "")
        {
            case "shuttle":
                new ShuttleStatusWindow().OpenCentered();
                break;
            case "cargo":
                new CargoRequestWindow().OpenCentered();
                break;
            case "notes":
                new CrewNotesWindow().OpenCentered();
                break;
            case "entry":
                var window = new FancyWindow { Title = "CrewEntryControl", MinSize = new(320, 120) };
                var list = new Robust.Client.UserInterface.Controls.BoxContainer
                {
                    Orientation = Robust.Client.UserInterface.Controls.BoxContainer.LayoutOrientation.Vertical,
                };
                for (var i = 0; i < 3; i++)
                    list.AddChild(new CrewEntryControl());
                window.ContentsContainer.AddChild(list);
                window.OpenCentered();
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(new[] { "shuttle", "cargo", "notes", "entry" })
            : CompletionResult.Empty;
    }
}
