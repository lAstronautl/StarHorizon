using Content.Client.Audio;
using Content.Client.Salvage;
using Content.Shared._NF.CCVar;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Content.Shared.Salvage.Expeditions;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._StarHorizon.Expedition;

/// <summary>
/// Локальная музыка экспедиции у игрока с ключом шифрования в гарнитуре (<see cref="ExpeditionMusicKeyComponent"/>).
/// Есть ключ - играет музыка по обстановке (капсула, фон, волна, эвакуация, победа), вынул - всё обрывается.
/// Всё происходит только на клиенте.
/// </summary>
public sealed class ExpeditionMusicSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private const float FadeTime = 2f;
    private const float KeyOffFadeTime = 0.4f;
    private const float MinVolume = -30f;
    private const float MaxVolume = 3f;
    private static readonly TimeSpan CritDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LossCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private enum Scene : byte
    {
        None,
        Pod,
        Ambient,
        Wave,
        Extraction,
        Win,
    }

    private ExpeditionMusicLibrary? _library;
    private Scene _scene;
    private bool _hasKey;
    private TimeSpan _nextCheck;

    private EntityUid? _stream;
    private readonly Queue<(string Path, bool Loop)> _queue = new();
    private Func<List<(string, bool)>>? _refill;

    private TimeSpan? _critSince;
    private TimeSpan _nextLoss;
    private EntityUid? _lossStream;

    /// <summary>У локального игрока в гарнитуре стоит ключ музыки экспедиции.</summary>
    public bool LocalHasKey => _hasKey;

    private ExpeditionMusicLibrary Library => _library ??= ExpeditionMusicLibrary.Load(_resources);

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayAmbientMusicEvent>(OnPlayAmbientMusic);
        Subs.CVar(_cfg, NFCCVars.SalvageExpeditionMusicVolume, _ => ApplyVolume(), true);
    }

    private void OnPlayAmbientMusic(ref PlayAmbientMusicEvent ev)
    {
        // Фоновая музыка игры не нужна, пока играет музыка экспедиции.
        if (_hasKey)
            ev.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextCheck)
        {
            ContinueChain();
            return;
        }

        _nextCheck = _timing.CurTime + CheckInterval;

        var hadKey = _hasKey;
        _hasKey = DetectKey();

        if (!_hasKey)
        {
            if (hadKey)
                StopEverything();
            return;
        }

        if (!hadKey)
            SilenceOldMusic();

        // Старый фон игры не должен проскакивать между треками.
        _contentAudio.DisableAmbientMusic();

        var scene = ComputeScene();
        if (scene != _scene)
            ChangeScene(scene);

        UpdateLoss();
        ContinueChain();
    }

    #region Detection

    private bool DetectKey()
    {
        if (_player.LocalEntity is not { } player
            || !_inventory.TryGetSlotEntity(player, "ears", out var headset)
            || !_container.TryGetContainer(headset.Value, EncryptionKeyHolderComponent.KeyContainerName, out var container))
        {
            return false;
        }

        foreach (var key in container.ContainedEntities)
        {
            if (HasComp<ExpeditionMusicKeyComponent>(key))
                return true;
        }

        return false;
    }

    private Scene ComputeScene()
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform))
            return Scene.None;

        // Игрок на эвакуационном шаттле, который ушёл в БСС.
        if (xform.GridUid is { } grid
            && TryComp<ExtractionShuttleMarkerComponent>(grid, out var marker)
            && marker.Departed)
        {
            return Scene.Win;
        }

        // Летит в дроп-поде: ожидание посадки.
        if (HasComp<DropPodVignetteComponent>(player))
            return Scene.Pod;

        if (xform.MapUid is not { } map || !HasComp<SalvageExpeditionComponent>(map))
            return Scene.None;

        if (TryComp<ExpeditionExtractionStateComponent>(map, out var extraction) && extraction.Active)
            return Scene.Extraction;

        if (TryComp<ExpeditionWaveStateComponent>(map, out var wave) && wave.Active)
            return Scene.Wave;

        return Scene.Ambient;
    }

    #endregion

    #region Scenes

    private void ChangeScene(Scene scene)
    {
        var previous = _scene;
        _scene = scene;

        _contentAudio.FadeOut(_stream, duration: FadeTime);
        _stream = null;
        _queue.Clear();
        _refill = null;

        var lib = Library;
        switch (scene)
        {
            case Scene.Pod:
                if (lib.Rig != null)
                    _queue.Enqueue((lib.Rig, true));
                break;

            case Scene.Ambient:
                // После посадки капсулы сначала стингер, потом фон по кругу.
                if (previous == Scene.Pod && lib.Tittle.Count > 0)
                    _queue.Enqueue((_random.Pick(lib.Tittle), false));
                _refill = () => Shuffled(lib.Ambiance);
                break;

            case Scene.Wave:
                if (lib.Wave.Count > 0)
                    _queue.Enqueue((_random.Pick(lib.Wave), false));
                if (lib.Intro != null)
                    _queue.Enqueue((lib.Intro, false));
                if (lib.Loop != null)
                    _queue.Enqueue((lib.Loop, true));
                break;

            case Scene.Extraction:
                _refill = () => Shuffled(lib.Extraction);
                break;

            case Scene.Win:
                if (lib.Win.Count > 0)
                    _queue.Enqueue((_random.Pick(lib.Win), false));
                break;
        }

        StartNext(fadeIn: true);
    }

    private List<(string, bool)> Shuffled(List<string> tracks)
    {
        var list = new List<string>(tracks);
        _random.Shuffle(list);
        return list.ConvertAll(t => (t, false));
    }

    private void ContinueChain()
    {
        if (!_hasKey || _scene == Scene.None)
            return;

        if (_stream != null && Exists(_stream.Value) && !Terminating(_stream.Value))
            return;

        StartNext(fadeIn: false);
    }

    private void StartNext(bool fadeIn)
    {
        _stream = null;

        if (_queue.Count == 0 && _refill != null)
        {
            foreach (var track in _refill())
                _queue.Enqueue(track);
        }

        if (_queue.Count == 0)
            return;

        var (path, loop) = _queue.Dequeue();
        var stream = _audio.PlayGlobal(path, Filter.Local(), false, AudioParams.Default.WithVolume(Volume()).WithLoop(loop));
        _stream = stream?.Entity;

        if (fadeIn)
            _contentAudio.FadeIn(_stream, duration: FadeTime);
    }

    #endregion

    #region Loss

    /// <summary>Игрок упал в крит и не встал за 5 секунд: играет проигрышная мелодия, не чаще раза в 5 минут.</summary>
    private void UpdateLoss()
    {
        if (_player.LocalEntity is not { } player || !TryComp<MobStateComponent>(player, out var state)
            || !_mobState.IsCritical(player, state))
        {
            _critSince = null;
            return;
        }

        var now = _timing.CurTime;
        _critSince ??= now;

        if (now - _critSince.Value < CritDelay || now < _nextLoss || Library.Loss.Count == 0)
            return;

        _nextLoss = now + LossCooldown;
        _lossStream = _audio.PlayGlobal(_random.Pick(Library.Loss), Filter.Local(), false,
            AudioParams.Default.WithVolume(Volume()))?.Entity;
    }

    #endregion

    private void StopEverything()
    {
        _contentAudio.FadeOut(_stream, duration: KeyOffFadeTime);
        _contentAudio.FadeOut(_lossStream, duration: KeyOffFadeTime);
        _stream = null;
        _lossStream = null;
        _queue.Clear();
        _refill = null;
        _scene = Scene.None;
        _critSince = null;
    }

    /// <summary>У игрока с ключом старая музыка конца экспедиции не играет: останавливаем уже запущенную.</summary>
    private void SilenceOldMusic()
    {
        var query = EntityQueryEnumerator<Content.Client.Salvage.SalvageExpeditionComponent>();
        while (query.MoveNext(out _, out var expedition))
        {
            if (expedition.Stream is not { } stream)
                continue;

            QueueDel(stream);
            expedition.Stream = null;
        }
    }

    private float Volume()
    {
        var value = AudioSystem.GainToVolume(_cfg.GetCVar(NFCCVars.SalvageExpeditionMusicVolume));
        return float.IsFinite(value) ? Math.Clamp(value, MinVolume, MaxVolume) : MinVolume;
    }

    private void ApplyVolume()
    {
        if (_stream is { } stream && Exists(stream))
            _audio.SetVolume(stream, Volume());
    }
}
