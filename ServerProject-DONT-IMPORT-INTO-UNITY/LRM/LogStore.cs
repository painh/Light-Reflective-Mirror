using System;
using System.Collections.Generic;
using System.Linq;

namespace LightReflectiveMirror
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }

    public class LogStore
    {
        private static LogStore _instance;
        public static LogStore Instance => _instance ??= new LogStore();

        private readonly List<LogEntry> _logs = new();
        private readonly object _lock = new();
        private long _nextId = 1;
        private const int MaxLogs = 10000;

        public void Add(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                var entry = new LogEntry
                {
                    Id = _nextId++,
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message
                };
                _logs.Add(entry);

                // 최대 개수 초과시 오래된 로그 삭제
                while (_logs.Count > MaxLogs)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 최근 로그 조회 (페이징 지원)
        /// </summary>
        /// <param name="count">가져올 로그 개수</param>
        /// <param name="beforeId">이 ID 이전의 로그만 가져옴 (null이면 가장 최근부터)</param>
        /// <returns>로그 목록 (최신순)</returns>
        public List<LogEntry> GetLogs(int count = 100, long? beforeId = null)
        {
            lock (_lock)
            {
                IEnumerable<LogEntry> query = _logs;

                if (beforeId.HasValue)
                {
                    query = query.Where(l => l.Id < beforeId.Value);
                }

                return query
                    .OrderByDescending(l => l.Id)
                    .Take(count)
                    .ToList();
            }
        }

        /// <summary>
        /// 저장된 전체 로그 수
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _logs.Count;
                }
            }
        }
    }
}
