using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Треки музыки экспедиции, разложенные по назначению. Группируются по имени файла в
/// <c>/Audio/_Horizon/PizzaTower</c>, поэтому новые файлы подхватываются без правки списков.
/// </summary>
public sealed class ExpeditionMusicLibrary
{
    public static readonly ResPath Folder = new("/Audio/_Horizon/PizzaTower");

    public const string RigFile = "_RIG_MoveItBoy_.ogg";
    public const string IntroFile = "_WD-INTRONOISE_.ogg";
    public const string LoopFile = "_WD-NOISEANTHEM-LOOP_.ogg";

    /// <summary>Фон на экспедиции, когда нет волны.</summary>
    public readonly List<string> Ambiance = new();

    /// <summary>Стингер при приземлении дроп-пода.</summary>
    public readonly List<string> Tittle = new();

    /// <summary>Музыка волны (<c>_CI_*</c> и <c>_WD_*</c>).</summary>
    public readonly List<string> Wave = new();

    public readonly List<string> Extraction = new();
    public readonly List<string> Win = new();
    public readonly List<string> Loss = new();

    public string? Rig;
    public string? Intro;
    public string? Loop;

    public static ExpeditionMusicLibrary Load(IResourceManager resources)
    {
        return Group(resources.ContentFindFiles(Folder));
    }

    public static ExpeditionMusicLibrary Group(IEnumerable<ResPath> files)
    {
        var library = new ExpeditionMusicLibrary();
        foreach (var file in files)
        {
            var name = file.Filename;
            if (!name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = file.ToString();
            if (name == RigFile)
                library.Rig = path;
            else if (name == IntroFile)
                library.Intro = path;
            else if (name == LoopFile)
                library.Loop = path;
            else if (name.StartsWith("_Ambiance_"))
                library.Ambiance.Add(path);
            else if (name.EndsWith("_Tittle.ogg"))
                library.Tittle.Add(path);
            else if (name.StartsWith("_CI_") || name.StartsWith("_WD_"))
                library.Wave.Add(path);
            else if (name.StartsWith("_EXTRACTION_"))
                library.Extraction.Add(path);
            else if (name.StartsWith("_WINSCREEN_"))
                library.Win.Add(path);
            else if (name.StartsWith("_LOSSSCREEN_"))
                library.Loss.Add(path);
        }

        return library;
    }
}
