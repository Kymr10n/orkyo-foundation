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
using Npgsql;

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
        var rows = await userRepository.GetAdminUserListAsync(search, status, ct);

        var users = new List<AdminUserSummary>();
        var keycloakIds = new List<(int index, string keycloakId)>();

        foreach (var row in rows)
        {
            if (row.KeycloakSub != null)
                keycloakIds.Add((users.Count, row.KeycloakSub));

            users.Add(row.Summary);
        }

        // Check site-admin role for each user with a Keycloak identity.
        // Failures are non-fatal — we want the user list even if Keycloak is degraded.
        foreach (var (index, keycloakId) in keycloakIds)
        {
            try
            {
                if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                    users[index] = users[index] with { IsSiteAdmin = true };
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to check site-admin role for {KeycloakId}", keycloakId);
            }
        }

        // Owned-tenant plan label is a commercial concept resolved by the edition.
        var ownedTenantIds = users.Where(u => u.OwnedTenantId.HasValue)
            .Select(u => u.OwnedTenantId!.Value).Distinct().ToList();
        if (ownedTenantIds.Count > 0)
        {
            var planInfo = await planInfoProvider.GetPlanInfoAsync(ownedTenantIds, ct);
            for (var i = 0; i < users.Count; i++)
            {
                if (users[i].OwnedTenantId is Guid otid && planInfo.TryGetValue(otid, out var info))
                    users[i] = users[i] with { OwnedTenantTier = info.PlanLabel };
            }
        }

        return Results.Ok(new { users });
    }

    private static async Task<IResult> GetUser(
        Guid userId,
        IPlatformUserRepository userRepository,
        IDbConnectionFactory connectionFactory,
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

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        if (user.OwnedTenantId is Guid ownedTenantId)
        {
            var planInfo = await planInfoProvider.GetPlanInfoAsync(new[] { ownedTenantId }, ct);
            if (planInfo.TryGetValue(ownedTenantId, out var info))
                user = user with { OwnedTenantTier = info.PlanLabel };
        }

        // Check site-admin role via Keycloak (best-effort — failures shouldn't hide the user)
        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId != null)
        {
            try
            {
                if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                    user = user with { IsSiteAdmin = true };
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to check site-admin role for {KeycloakId}", keycloakId);
            }
        }

        await using var identityCmd = new Npgsql.NpgsqlCommand(
            "SELECT id, provider, provider_subject, provider_email, created_at FROM user_identities WHERE user_id = @userId", conn);
        identityCmd.Parameters.AddWithValue("userId", userId);
        await using var identityReader = await identityCmd.ExecuteReaderAsync();
        var identityRows = new List<(Guid Id, string Provider, string ProviderSubject, string? ProviderEmail, DateTime CreatedAt)>();
        while (await identityReader.ReadAsync())
            identityRows.Add((identityReader.GetGuid(0), identityReader.GetString(1), identityReader.GetString(2), identityReader.IsDBNull(3) ? null : identityReader.GetString(3), identityReader.GetDateTime(4)));
        foreach (var (Id, Provider, ProviderSubject, ProviderEmail, CreatedAt) in identityRows)
        {
            user.Identities.Add(new AdminUserIdentity
            {
                Id = Id,
                Provider = Provider,
                ProviderSubject = ProviderSubject,
                ProviderEmail = ProviderEmail,
                CreatedAt = CreatedAt
            });
        }

        await identityReader.CloseAsync();

        await using var membershipCmd = new NpgsqlCommand(@"
            SELECT tm.tenant_id, t.slug, t.display_name, tm.role, tm.status, tm.created_at
            FROM tenant_memberships tm
            INNER JOIN tenants t ON tm.tenant_id = t.id
            WHERE tm.user_id = @userId
            ORDER BY t.display_name",
            conn);
        membershipCmd.Parameters.AddWithValue("userId", userId);

        await using var membershipReader = await membershipCmd.ExecuteReaderAsync();
        while (await membershipReader.ReadAsync())
        {
            user.Memberships.Add(new AdminUserMembership
            {
                TenantId = membershipReader.GetGuid(0),
                TenantSlug = membershipReader.GetString(1),
                TenantName = membershipReader.GetString(2),
                Role = membershipReader.GetString(3),
                Status = membershipReader.GetString(4),
                JoinedAt = membershipReader.GetDateTime(5)
            });
        }

        return Results.Ok(user);
    }

    private static async Task<IResult> GetUserMemberships(
        Guid userId,
        IPlatformUserRepository userRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT tm.tenant_id, t.slug, t.display_name, tm.role, tm.status, tm.created_at
            FROM tenant_memberships tm
            INNER JOIN tenants t ON tm.tenant_id = t.id
            WHERE tm.user_id = @userId
            ORDER BY t.display_name",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);

        var memberships = new List<AdminUserMembership>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            memberships.Add(new AdminUserMembership
            {
                TenantId = reader.GetGuid(0),
                TenantSlug = reader.GetString(1),
                TenantName = reader.GetString(2),
                Role = reader.GetString(3),
                Status = reader.GetString(4),
                JoinedAt = reader.GetDateTime(5)
            });
        }

        return Results.Ok(new { memberships });
    }

    private static async Task<IResult> DeactivateUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId != null)
        {
            try
            {
                await keycloak.DisableUserAsync(keycloakId);
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to disable user {UserId} in Keycloak", userId);
            }
        }

        await userService.SetGlobalStatusAsync(userId, UserStatusConstants.Disabled, ct);
        logger.LogInformation("Admin {AdminId} deactivated user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> ReactivateUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId != null)
        {
            try
            {
                await keycloak.EnableUserAsync(keycloakId);
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to enable user {UserId} in Keycloak", userId);
            }
        }

        await userService.SetGlobalStatusAsync(userId, UserStatusConstants.Active);
        logger.LogInformation("Admin {AdminId} reactivated user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId != null)
        {
            try
            {
                await keycloak.DeleteUserAsync(keycloakId);
            }
            catch (KeycloakAdminException ex)
            {
                logger.LogWarning(ex, "Failed to delete user {UserId} from Keycloak", userId);
            }
        }

        await userService.PermanentlyDeleteAsync(userId, ct);
        logger.LogInformation("Admin {AdminId} permanently deleted user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    /// <summary>
    /// Looks up the Keycloak provider_subject for a user via user_identities.
    /// Returns null if the user has no Keycloak identity; Keycloak operations are best-effort.
    /// </summary>
    private static async Task<string?> GetKeycloakIdAsync(Guid userId, IDbConnectionFactory connectionFactory, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT provider_subject FROM user_identities WHERE user_id = @userId AND provider = 'keycloak' LIMIT 1", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private static async Task<IResult> PromoteSiteAdmin(
        Guid userId,
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId == null)
            return ErrorResponses.UnprocessableEntity("User has no Keycloak identity — cannot manage realm roles");

        try
        {
            if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                return ErrorResponses.Conflict("User already has the site-admin role");

            await keycloak.AssignRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return ErrorResponses.UnprocessableEntity("Keycloak identity is stale — user not found in identity provider");
        }

        logger.LogInformation("Admin {AdminId} promoted user {UserId} to site-admin", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSiteAdmin(
        Guid userId,
        IPlatformUserRepository userRepository,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct = default)
    {
        // Prevent revoking your own site-admin role
        if (userId == principal.UserId)
            return ErrorResponses.BadRequest("Cannot revoke your own site-admin role");

        if (!await userRepository.ExistsAsync(userId, ct))
            return ErrorResponses.NotFound("User");

        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId == null)
            return ErrorResponses.UnprocessableEntity("User has no Keycloak identity — cannot manage realm roles");

        try
        {
            if (!await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                return ErrorResponses.Conflict("User does not have the site-admin role");

            // Prevent revoking the last site-admin
            var memberCount = await keycloak.CountRealmRoleMembersAsync(KeycloakClaims.SiteAdminRole);
            if (memberCount <= 1)
                return ErrorResponses.BadRequest("Cannot revoke the last site-admin. Promote another user first.");

            await keycloak.RevokeRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return ErrorResponses.UnprocessableEntity("Keycloak identity is stale — user not found in identity provider");
        }

        logger.LogInformation("Admin {AdminId} revoked site-admin from user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

}
