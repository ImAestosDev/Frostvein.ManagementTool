namespace Fmt.Core;

public sealed class LandOfLifeSettings
{
    public bool Visible { get; set; } = true;
    public string Name { get; set; } = "Land der Lebenden";
    public int WindowMinutes { get; set; } = 120;
    public int MarkerOffsetMinutes { get; set; } = 60;
    public string MarkerLabel { get; set; } = "Event";
}

public sealed record ScheduleSlot(int StartMinute, int EndMinute, int MarkerMinute);

public sealed record ScheduleState(int SlotIndex, TimeSpan LeftInSlot, double SlotProgress, DateTime NextMarker);

// A day split into equal windows, each with one marked point in time.
public static class WindowSchedule
{
    private const int MinutesPerDay = 1440;
    private const int ShortestWindow = 15;

    public static List<ScheduleSlot> Slots(int windowMinutes, int markerOffset)
    {
        var window = Math.Clamp(windowMinutes, ShortestWindow, MinutesPerDay);
        var offset = Math.Clamp(markerOffset, 0, window - 1);
        var slots = new List<ScheduleSlot>();
        for (var start = 0; start < MinutesPerDay; start += window)
            slots.Add(new ScheduleSlot(start, Math.Min(start + window, MinutesPerDay), start + offset));
        return slots;
    }

    public static ScheduleState StateAt(DateTime now, List<ScheduleSlot> slots)
    {
        var minute = now.TimeOfDay.TotalMinutes;
        var index = slots.FindIndex(s => minute >= s.StartMinute && minute < s.EndMinute);
        var slot = slots[index];
        var length = slot.EndMinute - slot.StartMinute;
        return new ScheduleState(index, TimeSpan.FromMinutes(slot.EndMinute - minute), (minute - slot.StartMinute) / length, NextMarker(now, slots));
    }

    private static DateTime NextMarker(DateTime now, List<ScheduleSlot> slots)
    {
        foreach (var s in slots)
        {
            var at = now.Date.AddMinutes(s.MarkerMinute);
            if (at > now) return at;
        }
        return now.Date.AddDays(1).AddMinutes(slots[0].MarkerMinute);
    }

    // 1440 reads as 0:00, like the in-game table.
    public static string Clock(int minute) => $"{minute / 60 % 24}:{minute % 60:00}";
}
