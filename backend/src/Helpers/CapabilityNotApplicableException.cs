namespace Api.Helpers;

/// <summary>
/// Exception thrown when attempting to attach a capability to a resource
/// but the criterion is not applicable to the resource's type.
/// </summary>
public class CapabilityNotApplicableException : Exception
{
    public Guid ResourceId { get; }
    public Guid CriterionId { get; }

    public CapabilityNotApplicableException(Guid resourceId, Guid criterionId, string message)
        : base(message)
    {
        ResourceId = resourceId;
        CriterionId = criterionId;
    }
}
