using System.Globalization;

namespace Fmt.Core;

public static class Calc
{
    private const int MaxDepth = 8;

    public static decimal? Numeric(Category category, Entry entry, FieldDef field, int depth = 0)
    {
        if (depth > MaxDepth) return null;
        if (field.Type == FieldType.Formula)
        {
            return Formula.Evaluate(field.Formula, name =>
            {
                var target = category.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                return target is null ? null : Numeric(category, entry, target, depth + 1);
            });
        }
        if (!entry.Values.TryGetValue(field.Id, out var raw) || string.IsNullOrEmpty(raw)) return null;
        return field.Type switch
        {
            FieldType.Gold or FieldType.Number or FieldType.Minutes =>
                decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
            FieldType.Bool => raw == "1" ? 1m : 0m,
            _ => null,
        };
    }

    public static long? GoldValue(Category category, Entry entry)
    {
        var field = category.Fields.FirstOrDefault(f => f.Id == category.GoldFieldId);
        var value = field is null ? null : Numeric(category, entry, field);
        return value is null ? null : (long)decimal.Round(value.Value, 0, MidpointRounding.AwayFromZero);
    }

    // Formula gold field still empty, e.g. a trade that is not sold yet.
    public static bool IsOpen(Category category, Entry entry)
    {
        var field = category.Fields.FirstOrDefault(f => f.Id == category.GoldFieldId);
        return field?.Type == FieldType.Formula && Numeric(category, entry, field) is null;
    }

    public static string Display(Category category, Entry entry, FieldDef field)
    {
        if (field.Type == FieldType.Formula)
        {
            var v = Numeric(category, entry, field);
            if (v is null) return "";
            return field.FormulaIsGold
                ? Gold.Format((long)decimal.Round(v.Value, 0, MidpointRounding.AwayFromZero))
                : v.Value.ToString("0.##", new CultureInfo("de-DE"));
        }
        if (!entry.Values.TryGetValue(field.Id, out var raw) || string.IsNullOrEmpty(raw)) return "";
        return field.Type switch
        {
            FieldType.Gold => long.TryParse(raw, out var g) ? Gold.Format(g) : raw,
            FieldType.Minutes => int.TryParse(raw, out var m) ? Duration.Format(m) : raw,
            FieldType.Number => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                ? n.ToString("0.##", new CultureInfo("de-DE")) : raw,
            FieldType.Bool => raw == "1" ? "Ja" : "Nein",
            _ => raw,
        };
    }

    // First text-like field names the entry in lists.
    public static string Title(Category category, Entry entry)
    {
        foreach (var f in category.Fields.Where(f => f.Type is FieldType.Text or FieldType.Select))
        {
            var text = Display(category, entry, f);
            if (text.Length > 0) return text;
        }
        return category.Name;
    }
}

public static class Stats
{
    public static DateOnly GameDay(DateTime now, int resetHour) => DateOnly.FromDateTime(now.AddHours(-resetHour));

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    public static long Net(AppData data, DateOnly from, DateOnly to)
    {
        long sum = 0;
        foreach (var e in data.Entries)
        {
            if (e.Date < from || e.Date > to) continue;
            var c = data.FindCategory(e.CategoryId);
            if (c is { CountsInTotal: true }) sum += Calc.GoldValue(c, e) ?? 0;
        }
        return sum;
    }

    public static long[] DailySeries(AppData data, DateOnly lastDay, int days)
    {
        var result = new long[days];
        var first = lastDay.AddDays(-(days - 1));
        foreach (var e in data.Entries)
        {
            if (e.Date < first || e.Date > lastDay) continue;
            var c = data.FindCategory(e.CategoryId);
            if (c is { CountsInTotal: true }) result[e.Date.DayNumber - first.DayNumber] += Calc.GoldValue(c, e) ?? 0;
        }
        return result;
    }

    public static List<(Category Category, long Gold)> ByCategory(AppData data, DateOnly from, DateOnly to)
    {
        var totals = data.Categories.Where(c => c.CountsInTotal).ToDictionary(c => c.Id, _ => 0L);
        foreach (var e in data.Entries)
        {
            if (e.Date < from || e.Date > to || !totals.ContainsKey(e.CategoryId)) continue;
            totals[e.CategoryId] += Calc.GoldValue(data.FindCategory(e.CategoryId)!, e) ?? 0;
        }
        return totals.Where(t => t.Value != 0)
            .Select(t => (data.FindCategory(t.Key)!, t.Value))
            .OrderByDescending(t => Math.Abs(t.Value)).ToList();
    }

    public static string PeriodKey(Recurrence r, DateOnly day)
    {
        if (r == Recurrence.Daily) return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}";
    }

    private static string CheckOwner(DailyTask task, string character) => task.PerCharacter ? character : "";

    public static bool IsDone(AppData data, DailyTask task, string character, DateOnly day)
    {
        var period = PeriodKey(task.Recurrence, day);
        var owner = CheckOwner(task, character);
        return data.Checks.Any(c => c.TaskId == task.Id && c.Period == period && c.Character == owner);
    }

    public static void SetDone(AppData data, DailyTask task, string character, DateOnly day, bool done)
    {
        var period = PeriodKey(task.Recurrence, day);
        var owner = CheckOwner(task, character);
        data.Checks.RemoveAll(c => c.TaskId == task.Id && c.Period == period && c.Character == owner);
        if (done) data.Checks.Add(new TaskCheck { TaskId = task.Id, Period = period, Character = owner });
    }

    public static (int Done, int Total) Progress(AppData data, string character, DateOnly day, Recurrence recurrence)
    {
        var tasks = data.Tasks.Where(t => t.Active && t.Recurrence == recurrence && t.CreatedDay <= day).ToList();
        return (tasks.Count(t => IsDone(data, t, character, day)), tasks.Count);
    }

    // Consecutive fully finished days; an unfinished today does not break it.
    public static int Streak(AppData data, string character, DateOnly today)
    {
        var (doneToday, totalToday) = Progress(data, character, today, Recurrence.Daily);
        if (totalToday == 0) return 0;
        var streak = doneToday == totalToday ? 1 : 0;
        var day = today;
        for (var i = 0; i < 3650; i++)
        {
            day = day.AddDays(-1);
            var (done, total) = Progress(data, character, day, Recurrence.Daily);
            if (total == 0 || done < total) break;
            streak++;
        }
        return streak;
    }
}
