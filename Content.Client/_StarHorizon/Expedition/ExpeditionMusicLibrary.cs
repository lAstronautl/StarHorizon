using Content.Shared._StarHorizon.Expedition;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Треки музыки экспедиции одного ключа: списки файлов по сценам, развёрнутые из прототипа ключа
/// (<see cref="ExpeditionMusicKeyComponent"/>): отдельный файл или все файлы коллекции звуков.
/// </summary>
public sealed class ExpeditionMusicLibrary
{
    public readonly List<string> Rig = new();
    public readonly List<string> Tittle = new();
    public readonly List<string> Ambiance = new();
    public readonly List<string> Wave = new();
    public readonly List<string> Extraction = new();
    public readonly List<string> Win = new();
    public readonly List<string> Loss = new();

    public static ExpeditionMusicLibrary FromKey(ExpeditionMusicKeyComponent key, IPrototypeManager proto)
    {
        var library = new ExpeditionMusicLibrary();
        Fill(library.Rig, key.Rig, proto);
        Fill(library.Tittle, key.Tittle, proto);
        Fill(library.Ambiance, key.Ambiance, proto);
        Fill(library.Wave, key.Wave, proto);
        Fill(library.Extraction, key.Extraction, proto);
        Fill(library.Win, key.Win, proto);
        Fill(library.Loss, key.Loss, proto);
        return library;
    }

    private static void Fill(List<string> list, SoundSpecifier? specifier, IPrototypeManager proto)
    {
        switch (specifier)
        {
            case SoundPathSpecifier path:
                list.Add(path.Path.ToString());
                break;

            case SoundCollectionSpecifier { Collection: { } id }
                when proto.TryIndex<SoundCollectionPrototype>(id, out var collection):
                foreach (var file in collection.PickFiles)
                    list.Add(file.ToString());
                break;
        }
    }
}
