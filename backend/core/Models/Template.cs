namespace Api.Models;

public class Template
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntityType { get; set; } = string.Empty;

    // Request-specific fields (only used when EntityType = 'request')
    public int? DurationValue { get; set; }
    public string? DurationUnit { get; set; }
    public bool FixedStart { get; set; } = false;
    public bool FixedEnd { get; set; } = false;
    public bool FixedDuration { get; set; } = true;

    /// <summary>
    /// The resource types a request made from this template needs, for request templates.
    /// Copied onto the request when the template is applied, and what a routing step's
    /// operation gets scheduled onto.
    /// </summary>
    public IReadOnlyList<string> TargetResourceTypeKeys { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TemplateItem
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid CriterionId { get; set; }
    public string Value { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Joined fields from criteria table
    public string? CriterionName { get; set; }
    public string? CriterionDataType { get; set; }
    public string? CriterionCategory { get; set; }
}

// API request/response models
public class CreateTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntityType { get; set; } = string.Empty;

    // Request-specific fields
    public int? DurationValue { get; set; }
    public string? DurationUnit { get; set; }
    public bool FixedStart { get; set; } = false;
    public bool FixedEnd { get; set; } = false;
    public bool FixedDuration { get; set; } = true;

    /// <summary>Request templates only. Omit or empty for a template that targets nothing.</summary>
    public IReadOnlyList<string>? TargetResourceTypeKeys { get; set; }
}

public class CreateTemplateItemRequest
{
    public Guid CriterionId { get; set; }
    public string Value { get; set; } = "{}";
}

public class UpdateTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntityType { get; set; } = string.Empty;

    // Request-specific fields
    public int? DurationValue { get; set; }
    public string? DurationUnit { get; set; }
    public bool FixedStart { get; set; } = false;
    public bool FixedEnd { get; set; } = false;
    public bool FixedDuration { get; set; } = true;

    /// <summary>Request templates only. Null leaves the types as they are; empty clears them.</summary>
    public IReadOnlyList<string>? TargetResourceTypeKeys { get; set; }
}
