namespace Api.Models.Reporting;

/// <summary>
/// Pagination request for reporting endpoints. Uses larger defaults than the
/// standard PageRequest to suit BI bulk-read patterns (500 / max 5000).
/// </summary>
public record ReportingPageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public const int DefaultPageSize = 500;
    public const int MaxPageSize = 5000;

    public ReportingPageRequest Sanitize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize),
    };

    public int Offset => (Math.Max(1, Page) - 1) * Math.Clamp(PageSize, 1, MaxPageSize);
}

/// <summary>Shared metadata block attached to every reporting response.</summary>
public record ReportingMetadata
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Envelope for paged reporting results. Wraps the existing PagedResult shape
/// and adds a metadata block for date range context.
/// </summary>
public record ReportingResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public required ReportingMetadata Metadata { get; init; }

    public static ReportingResult<T> Create(
        IReadOnlyList<T> items, int totalItems,
        ReportingPageRequest request, ReportingMetadata metadata)
    {
        var paged = request.Sanitize();
        return new ReportingResult<T>
        {
            Items = items,
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = totalItems,
            Metadata = metadata,
        };
    }
}

// ── Report DTOs ──────────────────────────────────────────────────────────────

public record SpaceUtilizationRow
{
    public string SiteName { get; init; } = "";
    public string SpaceName { get; init; } = "";
    public string? SpaceGroupName { get; init; }
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public double AvailableHours { get; init; }
    public double AllocatedHours { get; init; }
    public double UtilizationPercent { get; init; }
    public double OverbookedHours { get; init; }
    public int RequestCount { get; init; }
}

public record ResourceUtilizationRow
{
    public string ResourceType { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public string? ResourceGroupName { get; init; }
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public double AvailableHours { get; init; }
    public double AllocatedHours { get; init; }
    public double UtilizationPercent { get; init; }
    public double OverbookedHours { get; init; }
}

public record AllocationRow
{
    public string AllocationId { get; init; } = "";
    public string? RequestReference { get; init; }
    public string RequestTitle { get; init; } = "";
    public string ResourceType { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public string? SiteName { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public double DurationHours { get; init; }
    public string Status { get; init; } = "";
    public DateTime UpdatedAtUtc { get; init; }
}

public record RequestThroughputRow
{
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public int CreatedCount { get; init; }
    public int InProgressCount { get; init; }
    public int CompletedCount { get; init; }
    public int CancelledCount { get; init; }
    public double? AverageLeadTimeHours { get; init; }
}

public record ConflictRow
{
    public string ConflictType { get; init; } = "Overbooking";
    public string ResourceType { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public string? RequestReference { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public double OverbookedHours { get; init; }
}

public record AbsenceRow
{
    public string ResourceType { get; init; } = "";
    public string? ResourceName { get; init; }
    public string? ResourceGroupName { get; init; }
    public string? AbsenceCategory { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public double AbsenceHours { get; init; }
}

public record CapacityVsDemandRow
{
    public string ResourceType { get; init; } = "";
    public string? ResourceGroupName { get; init; }
    public DateTime PeriodStartUtc { get; init; }
    public DateTime PeriodEndUtc { get; init; }
    public double AvailableHours { get; init; }
    public double DemandHours { get; init; }
    public double AllocatedHours { get; init; }
    public double UnallocatedDemandHours { get; init; }
    public double CapacityGapHours { get; init; }
}

// ── Query parameters ─────────────────────────────────────────────────────────

public record ReportingQuery
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public DateTime? UpdatedSince { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = ReportingPageRequest.DefaultPageSize;
    public string? Sort { get; init; }
    public string? Format { get; init; }
    public string? Granularity { get; init; }

    public ReportingPageRequest ToPageRequest() =>
        new ReportingPageRequest { Page = Page, PageSize = PageSize }.Sanitize();

    public ReportingMetadata ToMetadata() => new()
    {
        FromUtc = From,
        ToUtc = To,
        GeneratedAtUtc = DateTime.UtcNow,
    };
}
