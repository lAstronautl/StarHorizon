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
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

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
    [Dependency] private readonly IResourceCache _cache = default!;
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
    private const float DimmedGain = 0.5f;
    private const float DimTime = 1.5f;
    private static readonly TimeSpan LossCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>За сколько до конца трека стартует следующий (бесшовный переход), секунды.</summary>
    public const float GaplessLead = 0.15f;

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
    private readonly record struct Track(string Path, bool Loop, bool Gapless = false);

    private readonly Queue<Track> _queue = new();
    private bool _currentGapless;
    private float _currentLength;
    private readonly Queue<string> _preload = new();
    private readonly HashSet<string> _preloaded = new();
    private readonly HashSet<string> _loaded = new();
    private string? _nextWaveTrack;
    private Func<List<Track>>? _refill;

    private float _dim = 1f;
    private float _dimTarget = 1f;
    private bool _wasDead;
    private TimeSpan _nextLoss;
    private EntityUid? _lossStream;

    /// <summary>У локального игрока в гарнитуре стоит ключ музыки экспедиции.</summary>
    public bool LocalHasKey => _hasKey;

    /// <summary>Файл уже загружен и декодирован заранее (переход на него не будет тормозить).</summary>
    public bool IsPreloaded(string path) => _loaded.Contains(path);

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
        UpdateDim(frameTime);

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
        {
            SilenceOldMusic();
            _contentAudio.DisableAmbientMusic();
            PreloadAhead();
        }

        // Декодирование музыки растянуто по времени: по одному файлу за проверку, а не в момент смены трека.
        if (_preload.TryDequeue(out var preloadPath))
        {
            try
            {
                _cache.GetResource<AudioResource>(new ResPath(preloadPath));
                _loaded.Add(preloadPath);
            }
            catch (Exception e)
            {
                Log.Warning($"Could not preload expedition music {preloadPath}: {e.Message}");
            }
        }

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
        _contentAudio.DisableAmbientMusic();

        var lib = Library;
        switch (scene)
        {
            case Scene.Pod:
                if (lib.Rig != null)
                    _queue.Enqueue(new Track(lib.Rig, true));
                break;

            case Scene.Ambient:
                // После посадки капсулы сначала стингер, потом фон по кругу.
                if (previous == Scene.Pod && lib.Tittle.Count > 0)
                    _queue.Enqueue(new Track(Take("tittle", lib.Tittle)!, false));
                _refill = () => Shuffled(lib.Ambiance);
                break;

            case Scene.Wave:
                if (lib.Wave.Count > 0)
                    _queue.Enqueue(new Track(Take("wave", lib.Wave)!, false, Gapless: lib.Intro != null));
                // Интро и петля идут встык: следующий трек стартует чуть раньше конца предыдущего.
                if (lib.Intro != null)
                    _queue.Enqueue(new Track(lib.Intro, false, Gapless: lib.Loop != null));
                if (lib.Loop != null)
                    _queue.Enqueue(new Track(lib.Loop, true));
                break;

            case Scene.Extraction:
                _refill = () => Shuffled(lib.Extraction);
                break;

            case Scene.Win:
                if (lib.Win.Count > 0)
                    _queue.Enqueue(new Track(Take("win", lib.Win)!, false));
                break;
        }

        StartNext(fadeIn: true);
    }

    private List<Track> Shuffled(List<string> tracks)
    {
        var list = new List<string>(tracks);
        _random.Shuffle(list);
        return list.ConvertAll(t => new Track(t, false, Gapless: true));
    }

    /// <summary>Пора ли стартовать следующий трек, чтобы не было паузы между ними.</summary>
    public static bool ShouldStartNext(float length, float position, float lead = GaplessLead)
    {
        return length > lead && position >= length - lead;
    }

    private void ContinueChain()
    {
        if (!_hasKey || _scene == Scene.None)
            return;

        if (_stream is { } stream && Exists(stream) && !Terminating(stream))
        {
            // Бесшовный переход: следующий (уже предзагруженный) трек стартует за долю секунды до конца текущего.
            if (_currentGapless
                && (_queue.Count > 0 || _refill != null)
                && TryComp<AudioComponent>(stream, out var audio)
                && ShouldStartNext(_currentLength, audio.PlaybackPosition))
            {
                StartNext(fadeIn: false);
            }

            return;
        }

        StartNext(fadeIn: false);
    }

    private void StartNext(bool fadeIn)
    {
        _stream = null;
        _currentGapless = false;

        if (_queue.Count == 0 && _refill != null)
        {
            foreach (var track in _refill())
                _queue.Enqueue(track);
        }

        if (_queue.Count == 0)
            return;

        var track0 = _queue.Dequeue();
        var stream = _audio.PlayGlobal(track0.Path, Filter.Local(), false, AudioParams.Default.WithVolume(Volume()).WithLoop(track0.Loop));
        _stream = stream?.Entity;
        _currentGapless = track0.Gapless && !track0.Loop;
        _currentLength = (float) _audio.GetAudioLength(new ResolvedPathSpecifier(new ResPath(track0.Path))).TotalSeconds;

        if (fadeIn)
            _contentAudio.FadeIn(_stream, duration: FadeTime);

        // Следующий трек заранее в кэш, чтобы переход не тормозил.
        if (_queue.Count == 0 && _refill != null)
        {
            foreach (var track in _refill())
                _queue.Enqueue(track);
        }

        if (_queue.TryPeek(out var next))
            Preload(next.Path);
    }

    #endregion

    #region Preload

    private void Preload(string? path)
    {
        if (path != null && _preloaded.Add(path))
            _preload.Enqueue(path);
    }

    /// <summary>Выбирает первый трек сцены заранее и ставит его в предзагрузку, вместе с повторным выбором на будущее.</summary>
    private string? Take(string kind, List<string> list)
    {
        if (list.Count == 0)
            return null;

        var picked = _pre.TryGetValue(kind, out var ahead) ? ahead : _random.Pick(list);
        _pre[kind] = _random.Pick(list);
        Preload(_pre[kind]);
        return picked;
    }

    private readonly Dictionary<string, string> _pre = new();

    /// <summary>При появлении ключа: заранее выбираем первые треки сцен и грузим всё, что понадобится на стыках.</summary>
    private void PreloadAhead()
    {
        var lib = Library;
        Preload(lib.Rig);
        Preload(lib.Intro);
        Preload(lib.Loop);

        foreach (var (kind, list) in new[] { ("tittle", lib.Tittle), ("wave", lib.Wave), ("win", lib.Win) })
        {
            if (list.Count == 0 || _pre.ContainsKey(kind))
                continue;

            _pre[kind] = _random.Pick(list);
            Preload(_pre[kind]);
        }

        if (lib.Extraction.Count > 0)
            Preload(_random.Pick(lib.Extraction));
        if (lib.Ambiance.Count > 0)
            Preload(_random.Pick(lib.Ambiance));
    }

    #endregion

    #region Loss

    /// <summary>
    /// В крите музыка плавно затухает до половины громкости (встал - возвращается). Проигрышная мелодия играет в момент
    /// смерти, не чаще раза в 5 минут.
    /// </summary>
    private void UpdateLoss()
    {
        var critical = false;
        var dead = false;
        if (_player.LocalEntity is { } player && TryComp<MobStateComponent>(player, out var state))
        {
            critical = _mobState.IsCritical(player, state);
            dead = _mobState.IsDead(player, state);
        }

        _dimTarget = critical || dead ? DimmedGain : 1f;

        var now = _timing.CurTime;
        if (dead && !_wasDead && now >= _nextLoss && Library.Loss.Count > 0)
        {
            _nextLoss = now + LossCooldown;
            _lossStream = _audio.PlayGlobal(_random.Pick(Library.Loss), Filter.Local(), false,
                AudioParams.Default.WithVolume(Volume(dim: false)))?.Entity;
        }

        _wasDead = dead;
    }

    /// <summary>Плавное приглушение каждый кадр (в крите до половины громкости).</summary>
    private void UpdateDim(float frameTime)
    {
        if (MathHelper.CloseTo(_dim, _dimTarget))
            return;

        var step = frameTime / DimTime * (1f - DimmedGain);
        _dim = _dim < _dimTarget ? MathF.Min(_dim + step, _dimTarget) : MathF.Max(_dim - step, _dimTarget);
        ApplyVolume();
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
        _dim = 1f;
        _dimTarget = 1f;
        _wasDead = false;
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

    private float Volume(bool dim = true)
    {
        var gain = _cfg.GetCVar(NFCCVars.SalvageExpeditionMusicVolume) * (dim ? _dim : 1f);
        var value = AudioSystem.GainToVolume(gain);
        return float.IsFinite(value) ? Math.Clamp(value, MinVolume, MaxVolume) : MinVolume;
    }

    private void ApplyVolume()
    {
        if (_stream is { } stream && Exists(stream))
            _audio.SetVolume(stream, Volume());
    }
}
