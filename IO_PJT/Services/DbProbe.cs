using System;
using System.Data.OleDb;
using System.IO;

namespace IO_PJT.Services
{
    /// <summary>
    /// Быстрая проверка доступности базы Access перед использованием (Часть C):
    /// пробное соединение с таймаутом 5 с + SELECT 1. Классифицирует типовые причины
    /// отказа (файл занят/эксклюзивно заблокирован, файл не найден, нет прав),
    /// чтобы пользователь получал понятное сообщение вместо зависаний или молчаливых ошибок.
    /// </summary>
    public static class DbProbe
    {
        /// <summary>Таймаут соединения, сек (короткий — быстрый отказ вместо ожидания ACE до 30 с).
        /// Задаётся свойством <see cref="OleDbConnection.ConnectionTimeout"/> перед Open:
        /// ACE OLE DB не принимает ключ Connect Timeout в строке соединения.</summary>
        public const int TimeoutSeconds = 5;

        /// <summary>Строка соединения OleDb (без таймаутов в строке — см. TimeoutSeconds).</summary>
        public static string ConnStr(string dbPath) =>
            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={dbPath};";

        /// <summary>Совместимое имя (исторически — строка «с таймаутом»); таймаут задаётся при открытии.</summary>
        public static string ConnStrWithTimeout(string dbPath) => ConnStr(dbPath);

        /// <summary>Открыть соединение (проверка/операции; таймаут — через TryOpen/CommandTimeout).</summary>
        public static OleDbConnection OpenConnection(string dbPath)
        {
            var cn = new OleDbConnection(ConnStr(dbPath));
            cn.Open();
            return cn;
        }

        /// <summary>Проверить, что база открывается и читается. false + текст ошибки — причина недоступности.
        /// Жёсткий предел ожидания — TimeoutSeconds секунд: если Open завис (блокировка файла),
        /// проверка не блокирует вызывающего, а возвращает «занята» (зависший поток освободится сам).</summary>
        public static bool TryOpen(string dbPath, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                error = "Путь к файлу базы не задан.";
                return false;
            }
            if (!File.Exists(dbPath))
            {
                error = $"Файл базы не найден: {dbPath}";
                return false;
            }
            try
            {
                var probe = Task.Run(() =>
                {
                    using var cn = new OleDbConnection(ConnStr(dbPath));
                    cn.Open();
                    using var cmd = new OleDbCommand("SELECT 1", cn);
                    cmd.CommandTimeout = TimeoutSeconds;
                    cmd.ExecuteScalar();
                });

                if (!probe.Wait(TimeSpan.FromSeconds(TimeoutSeconds)))
                {
                    error = $"Открытие базы не завершилось за {TimeoutSeconds} с — " +
                            "файл, вероятно, занят другим пользователем или программой.";
                    return false;
                }
                return true;
            }
            catch (AggregateException ae)
            {
                var inner = ae.InnerException;
                error = inner is OleDbException ole ? Classify(ole) : (inner?.Message ?? ae.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Человекочитаемая причина отказа по сообщению OleDb/ACE.</summary>
        private static string Classify(OleDbException ex) => ClassifyMessage(ex.Message);

        /// <summary>F9-фикс: классификация текста ошибки OleDb/ACE (и для исключений,
        /// обёрнутых вызывающим кодом с контекстом). Публично — используется окном импорта карты.</summary>
        public static string ClassifyMessage(string m)
        {
            if (ContainsAny(m, "locked", "in use", "exclusively", "excl",
                             "занят", "заблокирован", "эксклюз"))
                return "База данных занята другим пользователем или программой (возможно, открыта в эксклюзивном режиме). " +
                       "Закройте базу в другом приложении (Access, Excel, другая копия программы) и повторите попытку.\n\nТехническая причина: " + m;
            if (ContainsAny(m, "could not find file", "not a legal file name", "не найден", "не может найти",
                             "cannot find", "path"))
                return "Файл базы не найден или недоступен.\n\nТехническая причина: " + m;
            if (ContainsAny(m, "permission", "отказ в доступе", "доступ запрещ"))
                return "Нет прав доступа к файлу базы.\n\nТехническая причина: " + m;
            return "Не удалось открыть базу данных.\n\nТехническая причина: " + m;
        }

        /// <summary>F9-фикс: признак ошибки блокировки БД («locked»/«in use») — кандидат на повтор.</summary>
        public static bool IsLockError(string message) =>
            ContainsAny(message, "locked", "in use", "exclusively",
                        "занят", "заблокирован", "эксклюз");

        private static bool ContainsAny(string text, params string[] fragments)
        {
            foreach (var f in fragments)
                if (text.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}