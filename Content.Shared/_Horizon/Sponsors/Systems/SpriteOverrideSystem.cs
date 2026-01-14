using Robust.Shared.Utility;

namespace Content.Shared._Horizon.Sponsors.Systems
{
    public sealed class SpriteOverrideSystem : EntitySystem
    {
        // Словарь, где ключ - это ник игрока, а значение - спецификатор спрайта.
        private readonly Dictionary<string, SpriteSpecifier> _playerSprites = new()
        {
            // Пример записи для теста с использованием .rsi файла
            {"localhost@EvilBug", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/evilneko.rsi"), "state_name")},
            {"EvilBug", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/evilneko.rsi"), "state_name")},
            {"Joulerk", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/Joulerk.rsi"), "state_name")},
            {"Lemird", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/evilneko.rsi"), "state_name")},
            {"EXPERRIENCEE", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/joker.rsi"), "state_name")},
            {"Xigovir", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/uas.rsi"), "state_name")},
            {"TheGypsyBaron", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/baron.rsi"), "state_name")},
            {"Cvartet", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/cvartet.rsi"), "state_name")},
            {"DesBy", new SpriteSpecifier.Rsi(new ResPath("/Textures/_Horizon/Sponsors/Ghosts/kitsunes.rsi"), "state_name")},
        };

        /// <summary>
        /// Получаем спецификатор спрайта для игрока по его нику.
        /// </summary>
        /// <param name="playerName">Ник игрока</param>
        /// <returns>Спецификатор спрайта</returns>
        public SpriteSpecifier? GetSpriteForPlayer(string playerName)
        {
            if (_playerSprites.TryGetValue(playerName, out var spriteSpecifier))
            {
                return spriteSpecifier;
            }

            return null;
        }
    }
}
