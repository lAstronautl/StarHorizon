using System;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Tag;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;

namespace Content.Server._Horizon.Administration;

[AdminCommand(AdminFlags.Admin)]
public sealed class EntityCountNowCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private static readonly ISawmill _sawmill = Logger.GetSawmill("entityCountNow");

    public string Command => "entityCountNow";
    public string Description => "Counts entities for all existing tags and outputs the result to console and log (only tags with count > 0).";
    public string Help => "Usage: entityCountNow";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // Словарь для подсчета количества сущностей по каждому тегу
        var tagCounts = new Dictionary<string, int>();
        var query = _entityManager.EntityQueryEnumerator<TagComponent>();

        while (query.MoveNext(out var uid, out var tagComponent))
        {
            // Проходим по всем тегам текущей сущности
            foreach (var tag in tagComponent.Tags)
            {
                var tagString = tag.ToString();
                tagCounts.TryGetValue(tagString, out var currentCount);
                tagCounts[tagString] = currentCount + 1;
            }
        }

        // Фильтруем теги, где количество > 0, и сортируем по количеству (по убыванию)
        var filteredTags = tagCounts
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .ToList();

        string message;
        if (filteredTags.Count == 0)
        {
            message = "Не найдено сущностей с тегами.";
        }
        else
        {
            // Формируем сообщение с результатами
            var messageBuilder = new System.Text.StringBuilder();
            messageBuilder.AppendLine("Количество сущностей по тегам:");
            foreach (var (tag, count) in filteredTags)
            {
                messageBuilder.AppendLine($"  {tag}: {count}");
            }

            message = messageBuilder.ToString().TrimEnd();
        }

        // Отправляем результат в консоль
        shell.WriteLine(message);

        // Отправляем результат в лог
        _sawmill.Info($"{message.Replace(Environment.NewLine, " | ")}");
    }
}
