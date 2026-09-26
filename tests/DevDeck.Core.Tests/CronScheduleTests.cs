using DevDeck.Core.Scheduling;

namespace DevDeck.Core.Tests;

public class CronScheduleTests
{
    // Friday 2026-09-25 18:30.
    private static readonly DateTime Friday = new(2026, 9, 25, 18, 30, 0);

    private static DateTime? Next(string cron, DateTime after)
    {
        Assert.True(CronSchedule.TryParse(cron, out var schedule), cron);
        return schedule.Next(after);
    }

    [Fact]
    public void Weekday_mornings_skip_the_weekend()
    {
        Assert.Equal(new DateTime(2026, 9, 28, 6, 0, 0), Next("0 6 * * 1-5", Friday));
    }

    [Fact]
    public void Weekly_job_later_the_same_day()
    {
        Assert.Equal(new DateTime(2026, 9, 25, 19, 0, 0), Next("0 19 * * 5", Friday));
    }

    [Fact]
    public void Next_is_strictly_after_the_given_time()
    {
        Assert.Equal(new DateTime(2026, 9, 26, 18, 30, 0), Next("30 18 * * *", Friday));
    }

    [Fact]
    public void Steps_and_offsets()
    {
        Assert.Equal(new DateTime(2026, 9, 25, 18, 45, 0), Next("*/15 * * * *", Friday));
        Assert.Equal(new DateTime(2026, 9, 25, 18, 35, 0), Next("5/15 * * * *", Friday));
    }

    [Fact]
    public void Sunday_is_zero_or_seven()
    {
        Assert.Equal(new DateTime(2026, 9, 27, 9, 0, 0), Next("0 9 * * 0", Friday));
        Assert.Equal(new DateTime(2026, 9, 27, 9, 0, 0), Next("0 9 * * 7", Friday));
    }

    [Fact]
    public void Day_of_month_or_weekday_when_both_are_set()
    {
        // The 1st of the month or any Monday: Monday the 28th comes first.
        Assert.Equal(new DateTime(2026, 9, 28, 0, 0, 0), Next("0 0 1 * 1", Friday));
    }

    [Fact]
    public void Lists_and_month_rollover()
    {
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), Next("0 8 1,15 * *", Friday));
    }

    [Fact]
    public void Impossible_date_has_no_run()
    {
        Assert.Null(Next("0 0 31 2 *", Friday));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    public void Invalid_expressions_are_rejected(string cron)
    {
        Assert.False(CronSchedule.TryParse(cron, out _));
    }
}
