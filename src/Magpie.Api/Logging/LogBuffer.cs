using System;
using System.Collections.Generic;
using System.Threading;

using Magpie.Models.Api;

namespace Magpie.Api.Logging;

/// <summary>
/// Thread-safe circular buffer that retains recent application log entries for the logs API endpoint.
/// </summary>
public sealed class LogBuffer
{
    private const int Capacity = 1000;

    private readonly LogEntry[] _buffer = new LogEntry[Capacity];
    private int _head;
    private int _count;
    private readonly Lock _lock = new();

    /// <summary>
    /// Adds a log entry to the buffer. If the buffer is full the oldest entry is overwritten.
    /// </summary>
    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of all retained entries in chronological order (oldest first).
    /// </summary>
    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                return [];
            }

            var result = new LogEntry[_count];

            if (_count < Capacity)
            {
                // Buffer has not wrapped -- entries run from index 0 to _count-1.
                Array.Copy(_buffer, 0, result, 0, _count);
            }
            else
            {
                // Buffer has wrapped -- oldest entry is at _head.
                var tail = Capacity - _head;
                Array.Copy(_buffer, _head, result, 0, tail);
                Array.Copy(_buffer, 0, result, tail, _head);
            }

            return result;
        }
    }
}
