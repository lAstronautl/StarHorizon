using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared._Horizon.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.TrashCleanup;

/// <summary>
/// Система автоматического удаления мусорных сущностей через настраиваемое время.
/// Активируется только после задержки от начала раунда.
/// </summary>
public sealed class TrashCleanupSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private bool _enabled;
    private float _lifetime;
    private float _startDelay;

    /// <summary>
    /// Время начала текущего раунда.
    /// </summary>
    private TimeSpan _roundStartTime;

    /// <summary>
    /// Активна ли система (после истечения задержки).
    /// </summary>
    private bool _isActive;

    /// <summary>
    /// Теги, определяющие сущности для очистки.
    /// </summary>
    private static readonly string[] CleanupTags = { "Trash", "Cartridge" };

    /// <summary>
    /// Префикс ID прототипа для мусорных сущностей.
    /// </summary>
    private const string TrashPrefix = "Trash";

    /// <summary>
    /// Как часто проверять истёкший мусор (в секундах).
    /// </summary>
    private const float CheckInterval = 5f;

    private float _timeSinceLastCheck;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(HorizonCCVars.TrashCleanupEnabled, OnEnabledChanged, true);
        _cfg.OnValueChanged(HorizonCCVars.TrashCleanupLifetime, OnLifetimeChanged, true);
        _cfg.OnValueChanged(HorizonCCVars.TrashCleanupStartDelay, OnStartDelayChanged, true);

        // Подписываемся на события раунда
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        // Подписываемся на инициализацию TagComponent для отлова сущностей с тегом Trash
        SubscribeLocalEvent<TagComponent, MapInitEvent>(OnTagMapInit);
        // Подписываемся на инициализацию метаданных для отлова сущностей с префиксом Trash в ID прототипа
        SubscribeLocalEvent<MetaDataComponent, MapInitEvent>(OnMetaMapInit);
    }

    private void OnEnabledChanged(bool value)
    {
        _enabled = value;
        if (_enabled)
        {
            Log.Info("TrashCleanup: Система включена.");
            // Если раунд уже идёт и у нас нет времени старта, устанавливаем его сейчас
            if (_roundStartTime == TimeSpan.Zero && _gameTicker.RunLevel == GameRunLevel.InRound)
            {
                _roundStartTime = _timing.CurTime;
                Log.Info($"TrashCleanup: Раунд уже идёт. Система активируется через {_startDelay} секунд.");
            }
        }
        else
        {
            Log.Info("TrashCleanup: Система отключена.");
        }
    }

    private void OnLifetimeChanged(float value)
    {
        _lifetime = value;
        Log.Info($"TrashCleanup: Время жизни установлено на {value} секунд.");
    }

    private void OnStartDelayChanged(float value)
    {
        _startDelay = value;
        Log.Info($"TrashCleanup: Задержка старта установлена на {value} секунд.");
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _roundStartTime = _timing.CurTime;
        _isActive = false;

        if (_enabled)
            Log.Info($"TrashCleanup: Раунд {ev.Id} начался. Система активируется через {_startDelay} секунд.");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _isActive = false;
        _roundStartTime = TimeSpan.Zero;
    }

    private void OnTagMapInit(EntityUid uid, TagComponent component, MapInitEvent args)
    {
        if (!_enabled || !_isActive)
            return;

        // Проверяем, есть ли у сущности какой-либо из тегов очистки
        var hasCleanupTag = false;
        foreach (var tag in CleanupTags)
        {
            if (_tag.HasTag(uid, tag))
            {
                hasCleanupTag = true;
                break;
            }
        }

        if (!hasCleanupTag)
            return;

        AddTrashTimer(uid);
    }

    private void OnMetaMapInit(EntityUid uid, MetaDataComponent component, MapInitEvent args)
    {
        if (!_enabled || !_isActive)
            return;

        // Пропускаем, если таймер уже есть (от события тега)
        if (HasComp<TrashTimerComponent>(uid))
            return;

        // Проверяем, начинается ли ID прототипа с "Trash"
        var prototypeId = component.EntityPrototype?.ID;
        if (prototypeId == null || !prototypeId.StartsWith(TrashPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        AddTrashTimer(uid);
    }

    private void AddTrashTimer(EntityUid uid)
    {
        if (HasComp<TrashTimerComponent>(uid))
            return;

        // Не добавляем таймер предметам в контейнерах (в руках, рюкзаках и т.д.)
        if (_container.IsEntityInContainer(uid))
            return;

        var timer = EnsureComp<TrashTimerComponent>(uid);
        timer.DespawnTime = _timing.CurTime + TimeSpan.FromSeconds(_lifetime);

        var name = MetaData(uid).EntityName;
        Log.Debug($"TrashCleanup: Добавлен таймер для '{name}' ({uid}), будет удалён в {timer.DespawnTime}");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        // Проверяем, нужно ли активировать систему
        if (!_isActive && _roundStartTime != TimeSpan.Zero)
        {
            var timeSinceRoundStart = _timing.CurTime - _roundStartTime;
            if (timeSinceRoundStart.TotalSeconds >= _startDelay)
            {
                _isActive = true;
                Log.Info($"TrashCleanup: Система активирована после {_startDelay} секунд задержки.");
            }
            else
            {
                return; // Ещё ждём задержку
            }
        }

        if (!_isActive)
            return;

        _timeSinceLastCheck += frameTime;
        if (_timeSinceLastCheck < CheckInterval)
            return;

        _timeSinceLastCheck = 0f;

        var curTime = _timing.CurTime;
        var deletedCount = 0;

        var query = EntityQueryEnumerator<TrashTimerComponent>();
        while (query.MoveNext(out var uid, out var timer))
        {
            if (curTime < timer.DespawnTime)
                continue;

            // Не удаляем, если сущность в контейнере (в руках, рюкзаке и т.д.)
            if (_container.IsEntityInContainer(uid))
            {
                // Сбрасываем таймер - проверим позже, когда выбросят
                timer.DespawnTime = curTime + TimeSpan.FromSeconds(_lifetime);
                continue;
            }

            var name = MetaData(uid).EntityName;
            Log.Debug($"TrashCleanup: Удаление '{name}' ({uid}) - время жизни истекло.");
            QueueDel(uid);
            deletedCount++;
        }

        if (deletedCount > 0)
            Log.Info($"TrashCleanup: Удалено {deletedCount} мусорных сущностей.");
    }
}
