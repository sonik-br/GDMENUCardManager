namespace GDMENUCardManager.Core
{
    public static class Constants
    {
        public const string NameTextFile = "name.txt";
        public const string SerialTextFile = "serial.txt";
        //private const string InfoTextFile = "info.txt";
        public const string MenuConfigTextFile = "GDEMU.ini";
        public const string GdiShrinkBlacklistFile = "gdishrink_blacklist.txt";
        public const string PS1GameDBFile = "gamedb.json";
        public const string DefaultImageFileName = "disc";
        public const string Version = "v2.1.0";

        public static readonly string[] SupportedLanguages =
        {
            "en-US", //English (United States)
            "pt-BR", //Portuguese (Brasil)
            "es-ES", //Spanish (Spain)
            "ru-RU", //Russian (Russia)
            "fr-FR", //French (France)
            "zh-Hans", //Chinese (Simplified)
            //"zh-Hant", //Chinese (Traditional) not implemented
            "de-DE", //German (Germany)
            "ja-JP", //Japanese (Japan)
            "ko-KR", //Korean (Korea)
        };
    }
}
