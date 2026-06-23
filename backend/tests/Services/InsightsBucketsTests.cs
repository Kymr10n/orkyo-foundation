using Api.Services.Insights;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// Unit tests for the shared Insights bucketing strategy — calendar alignment, bucket generation,
/// and the per-bucket range caps. These boundaries are shared by every Insights chart, so they must
/// be exact and deterministic.
/// </summary>
public class InsightsBucketsTests
{
    [Theory]
    [InlineData("2026-06-17", "week", "2026-06-15")]   // Wed → Monday of that ISO week
    [InlineData("2026-06-01", "week", "2026-06-01")]   // already Monday
    [InlineData("2026-06-17", "month", "2026-06-01")]
    [InlineData("2026-05-17", "quarter", "2026-04-01")] // Q2 starts in April
    [InlineData("2026-02-17", "quarter", "2026-01-01")] // Q1 starts in January
    [InlineData("2026-11-17", "quarter", "2026-10-01")] // Q4 starts in October
    [InlineData("2026-06-17", "year", "2026-01-01")]
    public void AlignStart_TruncatesToBucketBoundary(string input, string bucket, string expected)
    {
        var ts = DateTime.Parse(input + "T13:45:00Z").ToUniversalTime();

        var aligned = InsightsBuckets.AlignStart(ts, bucket);

        Assert.Equal(DateTime.Parse(expected + "T00:00:00Z").ToUniversalTime(), aligned);
        Assert.Equal(DateTimeKind.Utc, aligned.Kind);
    }

    [Fact]
    public void Generate_ProducesContiguousAlignedBucketsCoveringTheRange()
    {
        var from = DateTime.Parse("2026-01-15T00:00:00Z").ToUniversalTime();
        var to = DateTime.Parse("2026-03-10T00:00:00Z").ToUniversalTime();

        var buckets = InsightsBuckets.Generate(from, to, "month");

        // Jan, Feb, Mar — starts at the month containing `from`, through the month containing `to`.
        Assert.Equal(3, buckets.Count);
        Assert.Equal(DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime(), buckets[0].Start);
        Assert.Equal(DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(), buckets[^1].End);
        // Contiguous: each bucket's end is the next bucket's start.
        for (var i = 1; i < buckets.Count; i++)
            Assert.Equal(buckets[i - 1].End, buckets[i].Start);
    }

    [Fact]
    public void Generate_WeekBucketsStartOnMonday()
    {
        var from = DateTime.Parse("2026-06-17T00:00:00Z").ToUniversalTime(); // Wednesday
        var to = DateTime.Parse("2026-06-30T00:00:00Z").ToUniversalTime();

        var buckets = InsightsBuckets.Generate(from, to, "week");

        Assert.All(buckets, b => Assert.Equal(DayOfWeek.Monday, b.Start.DayOfWeek));
        Assert.Equal(7, (buckets[0].End - buckets[0].Start).TotalDays);
    }

    [Fact]
    public void IndexOf_FindsContainingBucketAndReturnsMinusOneOutside()
    {
        var from = DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime();
        var to = DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime();
        var buckets = InsightsBuckets.Generate(from, to, "month");

        Assert.Equal(1, InsightsBuckets.IndexOf(buckets, DateTime.Parse("2026-02-14T08:00:00Z").ToUniversalTime()));
        Assert.Equal(-1, InsightsBuckets.IndexOf(buckets, DateTime.Parse("2025-12-31T23:00:00Z").ToUniversalTime()));
    }

    [Fact]
    public void MaxRangeDays_GrowsWithBucketCoarseness()
    {
        Assert.True(InsightsBuckets.MaxRangeDays("week") < InsightsBuckets.MaxRangeDays("month"));
        Assert.True(InsightsBuckets.MaxRangeDays("month") < InsightsBuckets.MaxRangeDays("quarter"));
        Assert.True(InsightsBuckets.MaxRangeDays("quarter") < InsightsBuckets.MaxRangeDays("year"));
    }

    [Fact]
    public void ValidSets_MatchTheContract()
    {
        Assert.Equal(new[] { "month", "quarter", "week", "year" }, InsightsBuckets.ValidBuckets.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "person", "space", "tool" }, InsightsBuckets.ValidResourceTypes.OrderBy(x => x).ToArray());
    }
}
