namespace IO_PJT
{
    /// <summary>Метаданные программы (задание 19.11.2026, п.6): текущая версия выводится
    /// в диалоге «Справка → О программе».</summary>
    public static class AppInfo
    {
        /// <summary>Текущая версия программы. Бампить после каждого изменения кода:
        /// F79, F80, … (F80 — битовые data-шаблоны: режим «Адрес»/«Адрес/бит» + FALSE).</summary>
        public const string Version = "F80";
    }
}