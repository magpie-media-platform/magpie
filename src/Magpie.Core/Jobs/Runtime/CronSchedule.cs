using System;

using Cronos;

namespace Magpie.Core.Jobs.Runtime;

internal readonly record struct CronSchedule
{
    private readonly CronExpression? _expression;

    private CronSchedule(CronExpression expression)
    {
        _expression = expression;
    }

    public static bool TryParse(string schedule, out CronSchedule result, out string? error)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(schedule))
        {
            error = "Schedule is required.";
            return false;
        }

        try
        {
            result = new CronSchedule(CronExpression.Parse(schedule.Trim()));
            return true;
        }
        catch (CronFormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool IsValid(string schedule)
    {
        return TryParse(schedule, out _, out _);
    }

    public DateTime? NextOccurrence(DateTime fromUtc)
    {
        if (_expression is null)
        {
            return null;
        }

        var from = fromUtc.Kind == DateTimeKind.Utc
            ? fromUtc
            : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);

        return _expression.GetNextOccurrence(from, TimeZoneInfo.Utc);
    }
}
