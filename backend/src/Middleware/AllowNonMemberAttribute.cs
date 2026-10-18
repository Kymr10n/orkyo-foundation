namespace Api.Middleware;

/// <summary>
/// Opts a tenant-scoped, authenticated route out of the membership gate in
/// <see cref="TenantMembershipMiddleware"/>. Membership is the default for every route that
/// resolves a tenant; this marks the few that a non-member legitimately calls (for example
/// reactivating a suspended tenant), so the exception is visible in the diff.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowNonMemberAttribute : Attribute, IAllowNonMember { }

public interface IAllowNonMember { }
