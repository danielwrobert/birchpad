namespace Birchpad.Core.Scheduling;

/// <summary>
/// A standard five-field cron expression (minute hour day-of-month month day-of-week),
/// evaluated in local time. Supports <c>*</c>, lists, ranges, and steps; day-of-week 0 and 7 are Sunday.
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] minutes = new bool[60];
    private readonly bool[] hours = new bool[24];
    private readonly bool[] days = new bool[32];
    private readonly bool[] months = new bool[13];
    private readonly bool[] weekdays = new bool[7];
    private bool dayRestricted;
    private bool weekdayRestricted;

    private CronSchedule(string expression)
    {
        this.Expression = expression;
    }

    public string Expression { get; }

    public static bool TryParse(string? expression, out CronSchedule schedule)
    {
        schedule = null!;
        var fields = expression?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is not { Length: 5 })
        {
            return false;
        }

        var result = new CronSchedule(expression!);
        if (!Fill(fields[0], 0, 59, result.minutes, out _)
            || !Fill(fields[1], 0, 23, result.hours, out _)
            || !Fill(fields[2], 1, 31, result.days, out result.dayRestricted)
            || !Fill(fields[3], 1, 12, result.months, out _)
            || !FillWeekdays(fields[4], result.weekdays, out result.weekdayRestricted))
        {
            return false;
        }

        schedule = result;
        return true;
    }

    /// <summary>The first run strictly after <paramref name="after"/>, or null if none falls within a year.</summary>
    public DateTime? Next(DateTime after)
    {
        var t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind).AddMinutes(1);
        var limit = after.AddYears(1);
        while (t <= limit)
        {
            if (!this.months[t.Month])
            {
                t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind).AddMonths(1);
            }
            else if (!this.DayMatches(t))
            {
                t = t.Date.AddDays(1);
            }
            else if (!this.hours[t.Hour])
            {
                t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind).AddHours(1);
            }
            else if (!this.minutes[t.Minute])
            {
                t = t.AddMinutes(1);
            }
            else
            {
                return t;
            }
        }

        return null;
    }

    // Cron rule: when both day fields are restricted, a day matching either one runs.
    private bool DayMatches(DateTime t)
    {
        var day = this.days[t.Day];
        var weekday = this.weekdays[(int)t.DayOfWeek];
        return this.dayRestricted && this.weekdayRestricted ? day || weekday
            : this.dayRestricted ? day
            : !this.weekdayRestricted || weekday;
    }

    private static bool FillWeekdays(string field, bool[] target, out bool restricted)
    {
        var expanded = new bool[8];
        if (!Fill(field, 0, 7, expanded, out restricted))
        {
            return false;
        }

        for (var i = 0; i < 7; i++)
        {
            target[i] = expanded[i];
        }

        target[0] |= expanded[7];
        return true;
    }

    private static bool Fill(string field, int min, int max, bool[] target, out bool restricted)
    {
        restricted = field != "*";
        foreach (var part in field.Split(','))
        {
            var step = 1;
            var range = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], out step) || step < 1)
                {
                    return false;
                }

                range = part[..slash];
            }

            int from, to;
            if (range == "*")
            {
                (from, to) = (min, max);
            }
            else if (range.Contains('-'))
            {
                var bounds = range.Split('-');
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out from) || !int.TryParse(bounds[1], out to))
                {
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(range, out from))
                {
                    return false;
                }

                // "5/15" means from 5 to the end in steps of 15.
                to = slash >= 0 ? max : from;
            }

            if (from < min || to > max || from > to)
            {
                return false;
            }

            for (var v = from; v <= to; v += step)
            {
                target[v] = true;
            }
        }

        return true;
    }
}
