using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Models;
using Api.Models.Admin;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Npgsql;

namespace Api.Endpoints.Admin;

public static class UserAdminEndpoints
{
    public static void MapUserAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());

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
        IDbConnectionFactory connectionFactory,
        IKeycloakAdminService keycloak,
        ILogger<EndpointLoggerCategory> logger,
        string? search = null,
        string? status = null)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClauses.Add("(LOWER(email) LIKE @search OR LOWER(display_name) LIKE @search)");
            parameters.Add(new NpgsqlParameter("search", $"%{search.ToLower()}%"));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClauses.Add("status = @status");
            parameters.Add(new NpgsqlParameter("status", status));
        }

        var whereClause = whereClauses.Count > 0 ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";

        var sql = $@"
            SELECT
                u.id, u.email, u.display_name, u.status, u.created_at, u.updated_at, u.last_login_at,
                (SELECT COUNT(*) FROM tenant_memberships tm WHERE tm.user_id = u.id AND tm.status = 'active') as membership_count,
                (SELECT COUNT(*) FROM user_identities ui WHERE ui.user_id = u.id) as identity_count,
                (SELECT ui.provider_subject FROM user_identities ui WHERE ui.user_id = u.id AND ui.provider = 'keycloak' LIMIT 1) as keycloak_sub,
                ot.id as owned_tenant_id,
                ot.tier as owned_tenant_tier
            FROM users u
            LEFT JOIN tenants ot ON ot.owner_user_id = u.id AND ot.status != 'deleting'
            {whereClause}
            ORDER BY u.email
            LIMIT 500";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        var users = new List<AdminUserSummary>();
        var keycloakIds = new List<(int index, string keycloakId)>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var keycloakSub = reader.IsDBNull(9) ? null : reader.GetString(9);
            if (keycloakSub != null)
                keycloakIds.Add((users.Count, keycloakSub));

            users.Add(new AdminUserSummary
            {
                Id = reader.GetGuid(0),
                Email = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5),
                LastLoginAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                MembershipCount = reader.GetInt32(7),
                IdentityCount = reader.GetInt32(8),
                IsSiteAdmin = false, // populated below
                OwnedTenantId = reader.IsDBNull(10) ? null : reader.GetGuid(10),
                OwnedTenantTier = reader.IsDBNull(11) ? null : ((ServiceTier)reader.GetInt32(11)).ToString()
            });
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

        logger.LogInformation("Admin listed {Count} users", users.Count);
        return Results.Ok(new { users });
    }

    private static async Task<IResult> GetUser(
        Guid userId,
        IDbConnectionFactory connectionFactory,
        IKeycloakAdminService keycloak,
        ILogger<EndpointLoggerCategory> logger)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var userCmd = new NpgsqlCommand(@"
            SELECT u.id, u.email, u.display_name, u.status, u.created_at, u.updated_at, u.last_login_at,
                   ot.id as owned_tenant_id, ot.tier as owned_tenant_tier
            FROM users u
            LEFT JOIN tenants ot ON ot.owner_user_id = u.id AND ot.status != 'deleting'
            WHERE u.id = @userId",
            conn);
        userCmd.Parameters.AddWithValue("userId", userId);

        await using var userReader = await userCmd.ExecuteReaderAsync();

        if (!await userReader.ReadAsync())
            return Results.NotFound(new { error = "User not found" });

        var user = new AdminUserDetail
        {
            Id = userReader.GetGuid(0),
            Email = userReader.GetString(1),
            DisplayName = userReader.IsDBNull(2) ? null : userReader.GetString(2),
            Status = userReader.GetString(3),
            CreatedAt = userReader.GetDateTime(4),
            UpdatedAt = userReader.GetDateTime(5),
            LastLoginAt = userReader.IsDBNull(6) ? null : userReader.GetDateTime(6),
            IsSiteAdmin = false,
            OwnedTenantId = userReader.IsDBNull(7) ? null : userReader.GetGuid(7),
            OwnedTenantTier = userReader.IsDBNull(8) ? null : ((ServiceTier)userReader.GetInt32(8)).ToString(),
            Identities = new List<AdminUserIdentity>(),
            Memberships = new List<AdminUserMembership>()
        };

        await userReader.CloseAsync();

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

        await using var identityCmd = UserIdentityLinkCommandFactory.CreateSelectIdentitiesByUserIdCommand(
            conn, userId);
        await using var identityReader = await identityCmd.ExecuteReaderAsync();
        var identityRows = await UserIdentityLinkReaderFlow.ReadIdentitiesAsync(identityReader);
        foreach (var row in identityRows)
        {
            user.Identities.Add(new AdminUserIdentity
            {
                Id = row.Id,
                Provider = row.Provider,
                ProviderSubject = row.ProviderSubject,
                ProviderEmail = row.ProviderEmail,
                CreatedAt = row.CreatedAt
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
        IDbConnectionFactory connectionFactory,
        ILogger<EndpointLoggerCategory> logger)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var checkCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @userId", conn);
        checkCmd.Parameters.AddWithValue("userId", userId);

        if (await checkCmd.ExecuteScalarAsync() == null)
            return Results.NotFound(new { error = "User not found" });

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
        ILogger<EndpointLoggerCategory> logger)
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

        await userService.SetGlobalStatusAsync(userId, "disabled");
        logger.LogInformation("Admin {AdminId} deactivated user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> ReactivateUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger)
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

        await userService.SetGlobalStatusAsync(userId, "active");
        logger.LogInformation("Admin {AdminId} reactivated user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUser(
        Guid userId,
        IUserManagementService userService,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger)
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

        await userService.PermanentlyDeleteAsync(userId);
        logger.LogInformation("Admin {AdminId} permanently deleted user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

    /// <summary>
    /// Looks up the Keycloak provider_subject for a user via user_identities.
    /// Returns null if the user has no Keycloak identity; Keycloak operations are best-effort.
    /// </summary>
    private static async Task<string?> GetKeycloakIdAsync(Guid userId, IDbConnectionFactory connectionFactory)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = UserIdentityLinkCommandFactory.CreateSelectKeycloakSubjectByUserIdCommand(conn, userId);
        return UserIdentityLinkScalarFlow.ReadKeycloakSubject(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> UserExistsAsync(Guid userId, IDbConnectionFactory connectionFactory)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = UserLookupByIdCommandFactory.CreateExistsByIdCommand(conn, userId);
        return UserLookupByIdScalarFlow.ReadExists(await cmd.ExecuteScalarAsync());
    }

    private static async Task<IResult> PromoteSiteAdmin(
        Guid userId,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger)
    {
        if (!await UserExistsAsync(userId, connectionFactory))
            return Results.NotFound(new { error = "User not found" });

        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId == null)
            return Results.UnprocessableEntity(new { error = "User has no Keycloak identity — cannot manage realm roles" });

        try
        {
            if (await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                return Results.Conflict(new { error = "User already has the site-admin role" });

            await keycloak.AssignRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return Results.UnprocessableEntity(new { error = "Keycloak identity is stale — user not found in identity provider" });
        }

        logger.LogInformation("Admin {AdminId} promoted user {UserId} to site-admin", principal.UserId, userId);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSiteAdmin(
        Guid userId,
        IKeycloakAdminService keycloak,
        IDbConnectionFactory connectionFactory,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger)
    {
        // Prevent revoking your own site-admin role
        if (userId == principal.UserId)
            return Results.BadRequest(new { error = "Cannot revoke your own site-admin role" });

        if (!await UserExistsAsync(userId, connectionFactory))
            return Results.NotFound(new { error = "User not found" });

        var keycloakId = await GetKeycloakIdAsync(userId, connectionFactory);
        if (keycloakId == null)
            return Results.UnprocessableEntity(new { error = "User has no Keycloak identity — cannot manage realm roles" });

        try
        {
            if (!await keycloak.HasRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole))
                return Results.Conflict(new { error = "User does not have the site-admin role" });

            // Prevent revoking the last site-admin
            var memberCount = await keycloak.CountRealmRoleMembersAsync(KeycloakClaims.SiteAdminRole);
            if (memberCount <= 1)
                return Results.BadRequest(new { error = "Cannot revoke the last site-admin. Promote another user first." });

            await keycloak.RevokeRealmRoleAsync(keycloakId, KeycloakClaims.SiteAdminRole);
        }
        catch (KeycloakAdminException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogWarning("Keycloak user {KeycloakId} not found for DB user {UserId} — stale identity link", keycloakId, userId);
            return Results.UnprocessableEntity(new { error = "Keycloak identity is stale — user not found in identity provider" });
        }

        logger.LogInformation("Admin {AdminId} revoked site-admin from user {UserId}", principal.UserId, userId);
        return Results.NoContent();
    }

}
