using System.Collections.Concurrent;
using System.IO;

namespace IO_PJT.Services
{
    /// <summary>
    /// Гарантия служебных таблиц перед обращением к базе (решение: «если таблиц нет —
    /// создавать автоматически при записи»). Идемпотентно: на сессию на каждую базу —
    /// один запрос схемы; повторная попытка — после Reset или при ошибке «таблицы нет».
    /// </summary>
    public static class ServiceSchema
    {
        private static readonly ConcurrentDictionary<string, byte> Ensured = new();

        /// <summary>Создать отсутствующие служебные таблицы (IO_LIST_Audit, MappingTemplates).</summary>
        public static void EnsureServiceTables(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return;
            if (Ensured.ContainsKey(dbPath)) return;
            try
            {
                new TableCreator(dbPath).EnsureServiceTables();
                Ensured[dbPath] = 0;
            }
            catch
            {
                // не блокируем операцию: следующая попытка при следующем обращении
            }
        }

        /// <summary>Сброс кэша (после ручного удаления таблицы пользователем).</summary>
        public static void Reset(string dbPath) => Ensured.TryRemove(dbPath, out _);
    }
}
