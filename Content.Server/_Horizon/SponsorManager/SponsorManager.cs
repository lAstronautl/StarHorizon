using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._Horizon.SponsorManager
{
    public sealed class SponsorManager
    {
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        private FileSystemWatcher _watcher = default!;

        private readonly string _sponsorsFilePath = "Resources/Prototypes/_Horizon/Sponsors/SponsorInfo/sponsors.txt";
        private readonly string _dsSponsorsFilePath = "Resources/Prototypes/_Horizon/Sponsors/SponsorInfo/discord_sponsors.txt";
        private readonly string _disposableFilePath = "Resources/Prototypes/_Horizon/Sponsors/SponsorInfo/disposable.txt";
        private readonly string _sponsorItemsFilePath = "Resources/Prototypes/_Horizon/Sponsors/SponsorInfo/sponsor_items.txt";

        private static HashSet<string> _sponsors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _sponsorsAndBalances = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _sponsorSlots = new(StringComparer.OrdinalIgnoreCase);

        #region Check files
        public void LoadSponsorsInfoFile()
        {
            EnsureFileExists(_dsSponsorsFilePath);
            EnsureFileExists(_sponsorsFilePath);
            EnsureFileExists(_disposableFilePath);
            EnsureFileExists(_sponsorItemsFilePath);
        }

        private void EnsureFileExists(string filePath)
        {
            var directoryPath = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath!);
            }

            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, string.Empty);
            }
        }
        #endregion Check files

        #region File Watcher
        public void FileWatcher()
        {
            _watcher = new FileSystemWatcher()
            {
                Path = Directory.GetCurrentDirectory() + @"/Resources/Prototypes/_Horizon/Sponsors/SponsorInfo",
                Filter = "discord_sponsors.txt",
                NotifyFilter = NotifyFilters.LastWrite,
            };

            _watcher.Changed += SyncSponsorsFiles;
            _watcher.EnableRaisingEvents = true;
        }
        #endregion File Watcher

        #region Discord
        private void SyncSponsorsFiles(object sender, FileSystemEventArgs e)
        {
            ReadSponsorsFile();

            var discordLines = SafeReadAllLines(_dsSponsorsFilePath);

            ProcessDiscordSponsors(discordLines);
        }

        private void ProcessDiscordSponsors(string[] discordLines)
        {
            var discordSponsors = new Dictionary<string, (string originalCkey, string discordId)>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in discordLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 3)
                    continue;

                var originalCkey = parts[1].Trim();
                var normalizedCkey = NormalizeUserName(originalCkey);
                var discordId = parts[2].Trim();

                if (string.IsNullOrWhiteSpace(normalizedCkey) || string.IsNullOrWhiteSpace(discordId))
                    continue;

                // Используем нормализованный ckey как ключ для поиска
                // Но сохраняем оригинальное имя для записи в файл
                discordSponsors[normalizedCkey] = (originalCkey, discordId);
            }

            var currentSponsors = new HashSet<string>(_sponsors, StringComparer.OrdinalIgnoreCase);

            // Добавляем или обновляем спонсоров из Discord
            foreach (var (normalizedCkey, (originalCkey, discordId)) in discordSponsors)
            {
                var slots = CalculateSlots(discordId);
                var tokens = CalculateTokens(discordId);

                if (currentSponsors.Contains(normalizedCkey))
                {
                    SaveSponsors(originalCkey, slots, tokens);
                }
                else
                {
                    AddSponsor(originalCkey, slots, tokens);
                }
            }

            // Удаляем спонсоров, которых больше нет в Discord списке
            foreach (var sponsor in currentSponsors)
            {
                if (!discordSponsors.ContainsKey(sponsor))
                {
                    RemoveSponsorFromFile(sponsor);
                }
            }
        }

        private string[] SafeReadAllLines(string filePath, int maxRetries = 3, int delay = 1000)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);

                    var lines = new List<string>();
                    string? line;

                    while ((line = sr.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                    return lines.ToArray();
                }
                catch (IOException)
                {
                    Task.Delay(delay).Wait();
                }
            }

            return Array.Empty<string>();
        }


        private int CalculateSlots(string discordId)
        {
            return discordId switch
            {
                "1349080752209395833" => 2, // Спонсор I - Авантюрист
                "1349080829334257856" => 5, // Спонсор II - Наемник
                "1349080858224623717" => 10, // Спонсор III - Шериф
                "1349080888927064216" => 20, // Спонсор IV - Представитель
                "1349080921537773568" => 20, // Спонсор V - Легенда
                "1349080947399725136" => 20, // Спонсор VI - пока нету
                _ => 0
            };
        }

        private int CalculateTokens(string discordId)
        {
            return discordId switch
            {
                "1349080829334257856" => 10, // Спонсор II - Наемник
                "1349080858224623717" => 15, // Спонсор III - Шериф
                "1349080888927064216" => 30, // Спонсор IV - Представитель
                "1349080921537773568" => 50, // Спонсор V - Легенда
                "1349080947399725136" => 100, // Спонсор VI - пока нету
                _ => 0
            };
        }
        #endregion Discord

        #region Read/Write File
        public void ReadSponsorsFile()
        {
            _sponsors.Clear();
            _sponsorsAndBalances.Clear();
            _sponsorSlots.Clear();

            foreach (var line in File.ReadLines(_sponsorsFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 3)
                    continue;

                var userName = NormalizeUserName(parts[0].Trim());
                if (string.IsNullOrWhiteSpace(userName))
                    continue;

                if (int.TryParse(parts[1], out var slots))
                {
                    _sponsorSlots[userName] = slots;
                }

                if (int.TryParse(parts[2], out var balance))
                {
                    _sponsorsAndBalances[userName] = balance;
                }

                _sponsors.Add(userName);
            }
        }

        public void AddSponsor(string userName, int slot, int token)
        {
            var normalizedName = NormalizeUserName(userName);
            _sponsors.Add(normalizedName);
            _sponsorSlots[normalizedName] = slot;
            _sponsorsAndBalances[normalizedName] = token;

            // Передаем оригинальное имя для сохранения в файл
            SaveSponsors(userName, slot, token);
        }

        public void SaveSponsors(string userName, int slot, int token)
        {
            var normalizedName = NormalizeUserName(userName);

            var lines = File.ReadAllLines(_sponsorsFilePath).ToList();
            var index = lines.FindIndex(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return false;

                var parts = line.Split(';');
                if (parts.Length == 0)
                    return false;

                var fileUserName = NormalizeUserName(parts[0].Trim());
                return fileUserName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase);
            });

            if (index != -1)
            {
                // Сохраняем оригинальное имя из файла, чтобы не менять регистр
                var existingLine = lines[index];
                var existingParts = existingLine.Split(';');
                var existingName = existingParts.Length > 0 ? existingParts[0].Trim() : userName;

                // Используем оригинальное имя из файла, если оно есть
                lines[index] = $"{existingName};{slot};{token}";
            }
            else
            {
                lines.Add($"{userName};{slot};{token}");
            }

            File.WriteAllLines(_sponsorsFilePath, lines);

            // В памяти используем нормализованное имя для консистентности
            _sponsors.Add(normalizedName);
            _sponsorSlots[normalizedName] = slot;
            _sponsorsAndBalances[normalizedName] = token;
        }

        public void RemoveSponsorFromFile(string userName)
        {
            var normalizedName = NormalizeUserName(userName);
            _sponsors.Remove(normalizedName);
            _sponsorsAndBalances.Remove(normalizedName);
            _sponsorSlots.Remove(normalizedName);

            var lines = File.ReadAllLines(_sponsorsFilePath).ToList();
            var index = lines.FindIndex(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return false;

                var parts = line.Split(';');
                if (parts.Length == 0)
                    return false;

                var fileUserName = NormalizeUserName(parts[0].Trim());
                return fileUserName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase);
            });

            if (index != -1)
            {
                lines.RemoveAt(index);
                File.WriteAllLines(_sponsorsFilePath, lines);
            }
        }
        #endregion Read/Write File

        #region Methods of finding
        private string NormalizeUserName(string userName)
        {
            return userName?.Trim() ?? string.Empty;
        }

        public bool IsSponsor(string userName)
        {
            var normalizedName = NormalizeUserName(userName);
            return _sponsors.Contains(normalizedName);
        }

        public int GetCharacterSlots(string userName)
        {
            var maxCharacterSlots = _cfg.GetCVar(CCVars.GameMaxCharacterSlots);
            var normalizedName = NormalizeUserName(userName);

            if (_sponsorSlots.TryGetValue(normalizedName, out var slot))
            {
                return maxCharacterSlots + slot;
            }

            return maxCharacterSlots;
        }

        public int GetBalance(string userName)
        {
            var normalizedName = NormalizeUserName(userName);
            if (_sponsorsAndBalances.TryGetValue(normalizedName, out var balance))
            {
                return balance;
            }

            return 0;
        }

        public void UpdateSponsorsAndBalances()
        {
            // Сначала обновляем данные из основного файла спонсоров
            // Это синхронизирует все три структуры данных: _sponsors, _sponsorsAndBalances, _sponsorSlots
            foreach (var line in File.ReadLines(_sponsorsFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length < 3)
                    continue;

                var userName = NormalizeUserName(parts[0].Trim());
                if (string.IsNullOrWhiteSpace(userName))
                    continue;

                // Добавляем/обновляем в HashSet спонсоров
                _sponsors.Add(userName);

                // Обновляем слоты
                if (int.TryParse(parts[1], out var slots))
                {
                    _sponsorSlots[userName] = slots;
                }

                // Обновляем балансы (начинаем с базового баланса из файла)
                if (int.TryParse(parts[2], out var balance))
                {
                    _sponsorsAndBalances[userName] = balance;
                }
            }

            // Затем добавляем дополнительные токены из disposable.txt
            var disposableLines = SafeReadAllLines(_disposableFilePath);

            foreach (var line in disposableLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 3)
                    continue;

                var ckey = NormalizeUserName(parts[0].Trim());
                if (string.IsNullOrWhiteSpace(ckey))
                    continue;

                if (int.TryParse(parts[2], out var additionalTokens))
                {
                    if (_sponsorsAndBalances.ContainsKey(ckey))
                    {
                        _sponsorsAndBalances[ckey] += additionalTokens;
                    }
                }
            }
        }


        public void DeductBalance(string userName, int cost)
        {
            var normalizedName = NormalizeUserName(userName);
            if (_sponsorsAndBalances.TryGetValue(normalizedName, out var balance))
            {
                if (balance >= cost)
                {
                    balance -= cost;
                    _sponsorsAndBalances[normalizedName] = balance;
                }
            }
        }

        public string GetColor(string userName)
        {
            var normalizedName = NormalizeUserName(userName);

            var lines = SafeReadAllLines(_dsSponsorsFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                var ckey = NormalizeUserName(parts[1].Trim());
                if (ckey.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length > 4)
                        return parts[4].Trim();
                }
            }

            return "#FF0000";
        }
        #endregion Methods of finding
    }
}
