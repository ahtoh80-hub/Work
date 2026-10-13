using System;
using System.Collections.Generic;

namespace IO_PJT.Services
{
    /// <summary>
    /// Простой потоковый журнал событий приложения.
    /// Уровни: Info (белый), Warning (жёлтый), Error (красный) — цвета задаются в UI.
    /// </summary>
    public sealed class EventLogger : IEventLogger
    {
        private readonly List<LogEntry> _entries = new();
        private readonly object _sync = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_sync) return _entries.ToArray(); }
        }

        public event Action<LogEntry>? EntryAdded;

        public void Info(string message) => Add(LogLevel.Info, message);
        public void Warn(string message) => Add(LogLevel.Warning, message);
        public void Error(string message) => Add(LogLevel.Error, message);

        public void Clear()
        {
            lock (_sync) _entries.Clear();
        }

        /// <summary>Ограничение журнала в памяти (последние записи).</summary>
        private const int MaxEntries = 10000;

        private void Add(LogLevel level, string message)
        {
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Message = message };
            lock (_sync)
            {
                _entries.Add(entry);
                if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
            EntryAdded?.Invoke(entry);
        }
    }
}
