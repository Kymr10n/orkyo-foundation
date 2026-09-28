using Api.Constants;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Models;
using Api.Models.Admin;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Microsoft.AspNetCore.Builder;

namespace Api.Endpoints.Admin;

public static class UserAdminEndpoints
{
    public static void MapUserAdminEndpoints(this WebApplication app)
    {
        var group = app.MapSiteAdminGroup();

        group.MapGet("/users", GetUsers)
            .RequireSiteAdmin()
            .WithName("AdminGetUsers")
            .WithSummary("List all users");

        group.MapGet("/users/{userId:guid}", GetUser)
            .RequireSiteAdmin()
            .WithName("AdminGetUser")
            .WithSummary("Get user by ID");

        group.MapGet("/users/{userId:guid}/memberships", GetUserMemberships)
            .RequireSiteAdmin()
            .WithName("AdminGetUserMemberships")
            .WithSummary("List all tenant memberships for a user");

        group.MapPost("/users/{userId:guid}/deactivate", DeactivateUser)
            .RequireSiteAdmin()
            .WithName("AdminDeactivateUser")
            .WithSummary("Disable a user account globally");

        group.MapPost("/users/{userId:guid}/reactivate", ReactivateUser)
            .RequireSiteAdmin()
            .WithName("AdminReactivateUser")
            .WithSummary("Re-enable a previously disabled user account");

        group.MapDelete("/users/{userId:guid}", DeleteUser)
            .RequireSiteAdmin()
            .WithName("AdminDeleteUser")
            .WithSummary("Permanently delete a user and all associated data");

        group.MapPost("/users/{userId:guid}/promote-site-admin", PromoteSiteAdmin)
            .RequireSiteAdmin()
            .WithName("AdminPromoteSiteAdmin")
            .WithSummary("Grant site-admin role to a user");

        group.MapPost("/users/{userId:guid}/revoke-site-admin", RevokeSiteAdmin)
            .RequireSiteAdmin()
            .WithName("AdminRevokeSiteAdmin")
            .WithSummary("Revoke site-admin role from a user");
    }

    private static async Task<IResult> GetUsers(
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        ITenantPlanInfoProvider planInfoProvider,
        ILogger<EndpointLoggerCategory> logger,
        string? search = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var list = await userRepository.GetAdminUserListAsync(search, status, ct);

        // One read of the site-admin role's members flags the whole page (it was one Keycloak
        // call per user). Failure is non-fatal — the list still comes back with no one flagged.
        IReadOnlySet<string> siteAdmins = new HashSet<string>();
        try
        {
            siteAdmins = await keycloak.GetRealmRoleMemberIdsAsync(KeycloakClaims.SiteAdminRole, ct);
        }
        catch (KeycloakAdminException ex)
        {
            logger.LogWarning(ex, "Failed to list the site-admin role's members");
        }

        var users = list.Items
            .Select(row => row.KeycloakSub is { } sub && siteAdmins.Contains(sub)
                ? row.Summary with { IsSiteAdmin = true }
                : row.Summary)
            .ToList();

        // Owned-tenant plan is a commercial concept resolved by the edition. Send the machine
        // CODE, not the display label — the admin UI feeds this straight into a select whose
        // option values are the lowercase codes.
        var ownedTenantIds = users.Where(u => u.OwnedTenantId.HasValue)
            .Select(u => u.OwnedTenantId!.Value).Distinct().ToList();
        if (ownedTenantIds.Count > 0)
        {
            var planInfo = await planInfoProvider.GetPlanInfoAsync(ownedTenantIds, ct);
            for (var i = 0; i < users.Count; i++)
            {
                if (users[i].OwnedTenantId is Guid otid && planInfo.TryGetValue(otid, out var info))
                    users[i] = users[i] with { OwnedTenantTier = info.PlanCode };
            }
        }

        // `users` stays the list the admin UI reads; the capped list's real total and its
        // truncation flag ride alongside, so a list cut at the cap says so.
        return Results.Ok(new { users, totalItems = list.TotalItems, hasNextPage = list.HasNextPage });
    }

    private static async Task<IResult> GetUser(
        Guid userId,
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        ITenantPlanInfoProvider planInfoProvider,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        var core = await userRepository.GetAdminUserCoreAsync(userId, ct);
        if (core is null)
            return ErrorResponses.NotFound("User");

        var user = new AdminUserDetail
        {
            Id = core.Id,
            Email = core.Email,
            DisplayName = core.DisplayName,
            Status = core.Status,
            CreatedAt = core.CreatedAt,
            UpdatedAt = core.UpdatedAt,
            LastLoginAt = core.LastLoginAt,
            IsSiteAdmin = false,
            OwnedTenantId = core.OwnedTenantId,
            OwnedTenantTier = null, // resolved below via the edition's plan provider
            Identities = new List<AdminUserIdentity>(),
            Memberships = new List<AdminUserMembership>()
        };

        if (user.OwnedTenantId is Guid ownedTenantId)
        {
            var planInfo = await planInfoProvider.GetPlanInfoAsync(new[] { ownedTenantId }, ct);
            if (planInfo.TryGetValue(ownedTenantId, out var info))
                user = user with { OwnedTenantTier = info.PlanCode };
        }

        // Check site-admin role via Keycloak (best-effort — failures shouldn't hide the user)
        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId != null)
        {
            try
            {
                if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole, ct))
                    user = user with { IsSiteAdmin = true };
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to check site-admin role for {KeycloakId}", keycloakId);
            }
        }

        user.Identities.AddRange(await userRepository.GetIdentitiesAsync(userId, ct));
        user.Memberships.AddRange(await userRepository.GetMembershipsAsync(userId, ct));

        return Results.Ok(user);
    }

    private static async Task<IResult> GetUserMemberships(
        Guid userId,
        IPlatformUserRepository userRepository,
        CancellationToken ct = default)
    {
        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        var memberships = await userRepository.GetMembershipsAsync(userId, ct);
        return Results.Ok(new { memberships });
    }

    private static async Task<IResult> DeactivateUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IPlatformUserRepository userRepository,
        IAdminAuditService audit,
        ICurrentPrincipal principal,
        CancellationToken ct = default)
    {
        // Keycloak first, and a failure fails the request: the DB must not say "disabled"
        // while the identity provider still lets the user sign in. An identity Keycloak no
        // longer has cannot sign in either, so a 404 there is the goal state.
        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId != null)
            await IgnoreKeycloakNotFoundAsync(() => keycloak.DisableUserAsync(keycloakId, ct));

        await userService.SetGlobalStatusAsync(userId, UserStatusConstants.Disabled, ct);
        await audit.RecordEventAsync(principal.UserIdOrNull, SecurityAuditActions.UserDeactivated, "user", userId.ToString(), ct: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReactivateUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IPlatformUserRepository userRepository,
        IAdminAuditService audit,
        ICurrentPrincipal principal,
        CancellationToken ct = default)
    {
        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId != null)
            await keycloak.EnableUserAsync(keycloakId, ct);

        await userService.SetGlobalStatusAsync(userId, UserStatusConstants.Active, ct);
        await audit.RecordEventAsync(principal.UserIdOrNull, SecurityAuditActions.UserReactivated, "user", userId.ToString(), ct: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IPlatformUserRepository userRepository,
        IAdminAuditService audit,
        ICurrentPrincipal principal,
        CancellationToken ct = default)
    {
        if (userId == principal.UserId)
            return ErrorResponses.BadRequest("Cannot delete your own account");

        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId != null)
            await IgnoreKeycloakNotFoundAsync(() => keycloak.DeleteUserAsync(keycloakId, ct));

        await userService.PermanentlyDeleteAsync(userId, ct);
        await audit.RecordEventAsync(principal.UserIdOrNull, SecurityAuditActions.UserDeleted, "user", userId.ToString(), ct: ct);
        return Results.NoContent();
    }

    private static async Task IgnoreKeycloakNotFoundAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            // Already gone from the identity provider.
        }
    }

    private static async Task<IResult> PromoteSiteAdmin(
        Guid userId,
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        IAdminAuditService audit,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId == null)
            return ErrorResponses.UnprocessableEntity("User has no Keycloak identity — cannot manage realm roles");

        try
        {
            if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole, ct))
                return ErrorResponses.Conflict("User already has the site-admin role");

            await keycloak.AssignRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole, ct);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return ErrorResponses.UnprocessableEntity("Keycloak identity is stale — user not found in identity provider");
        }

        await audit.RecordEventAsync(principal.UserIdOrNull, SecurityAuditActions.SiteAdminGranted, "user", userId.ToString(), ct: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSiteAdmin(
        Guid userId,
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        IAdminAuditService audit,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        // Prevent revoking your own site-admin role
        if (userId == principal.UserId)
            return ErrorResponses.BadRequest("Cannot revoke your own site-admin role");

        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        var keycloakId = await userRepository.GetKeycloakSubjectAsync(userId, ct);
        if (keycloakId == null)
            return ErrorResponses.UnprocessableEntity("User has no Keycloak identity — cannot manage realm roles");

        try
        {
            if (!await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole, ct))
                return ErrorResponses.Conflict("User does not have the site-admin role");

            // Prevent revoking the last site-admin
            var memberCount = await keycloak.CountRealmRoleMembersAsync(KeycloakClaims.SiteAdminRole, ct);
            if (memberCount <= 1)
                return ErrorResponses.BadRequest("Cannot revoke the last site-admin. Promote another user first.");

            await keycloak.RevokeRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole, ct);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return ErrorResponses.UnprocessableEntity("Keycloak identity is stale — user not found in identity provider");
        }

        await audit.RecordEventAsync(principal.UserIdOrNull, SecurityAuditActions.SiteAdminRevoked, "user", userId.ToString(), ct: ct);
        return Results.NoContent();
    }

}
