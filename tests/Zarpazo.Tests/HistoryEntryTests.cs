using Xunit;
using Zarpazo.History;

namespace Zarpazo.Tests;

public class HistoryEntryTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0);

    [Theory]
    [InlineData(2026, 11, 25, "Hoy, 00:05")]
    [InlineData(2026, 11, 24, "Ayer, 00:05")]
    [InlineData(2026, 11, 3, "03/11, 00:05")]
    [InlineData(2025, 11, 25, "25/11/2025")]
    public void Dates_are_told_from_today(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, HistoryEntry.DateLabel(new DateTime(year, month, day, 0, 5, 0), Now));
    }
}
