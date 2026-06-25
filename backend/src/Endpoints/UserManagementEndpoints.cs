using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class UserManagementEndpoints
{
    public static void MapUserManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("User Management");

        group.MapGet("/", GetAllUsers).WithName("GetAllUsers")
            .WithDescription("Get all users in the tenant (Admin only)");

        group.MapPost("/invite", InviteUser).WithName("InviteUser")
            .WithDescription("Invite a new user to the tenant (Admin only)")
            .Accepts<InviteUserRequest>("application/json");

        group.MapPatch("/{userId:guid}/role", UpdateUserRole).WithName("UpdateUserRole")
            .WithDescription("Update a user's role (Admin only)")
            .Accepts<UpdateUserRoleRequest>("application/json");

        group.MapDelete("/{userId:guid}", DeleteUser).WithName("DeleteUser")
            .WithDescription("Delete a user from the tenant (Admin only)");

        group.MapGet("/invitations", GetPendingInvitations).WithName("GetPendingInvitations")
            .WithDescription("Get all pending user invitations (Admin only)");

        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation).WithName("RevokeInvitation")
            .WithDescription("Revoke a pending invitation (Admin only)");

        // Public invitation endpoints (no auth, no tenant)
        app.MapGet("/api/invitations/validate", async ([FromServices] IInvitationService invitationService, [FromQuery] string? token, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(token))
                return ErrorResponses.BadRequest("Invalid invitation token");

            var (email, expiresAt, tenantName, error) = await invitationService.ValidateInvitationAsync(token, ct);
            return error != null
                ? ErrorResponses.BadRequest(error)
                : Results.Ok(new { email, expiresAt, tenantName });
        })
        .AllowAnonymous()
        .WithMetadata(new SkipTenantResolutionAttribute())
        .WithTags("User Management")
        .WithName("ValidateInvitation")
        .WithDescription("Validate an invitation token and get invitation details");

        app.MapPost("/api/invitations/accept", async ([FromServices] IInvitationService invitationService, AcceptInvitationRequest request, IValidator<AcceptInvitationRequest> validator, CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var (user, error) = await invitationService.AcceptInvitationAsync(request.Token, request.DisplayName, request.Password, ct);
                if (error != null) throw new ArgumentException(error);
                if (user == null) throw new ArgumentException("Invalid or expired invitation token");
                return new
                {
                    user = new { id = user!.Id, email = user.Email, displayName = user.DisplayName, role = user.Role.ToString().ToLowerInvariant() },
                    message = "Account created successfully. You can now log in."
                };
            }))
        .AllowAnonymous()
        .WithMetadata(new SkipTenantResolutionAttribute())
        .WithTags("User Management")
        .WithName("AcceptInvitation")
        .WithDescription("Accept a user invitation and create account")
        .Accepts<AcceptInvitationRequest>("application/json");
    }

    private static async Task<IResult> GetAllUsers(HttpContext context, IUserManagementService userManagementService, CancellationToken ct = default)
    {
        var tc = context.GetTenantContext();
        var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
        var users = await userManagementService.GetAllUsersAsync(org, ct);
        return Results.Ok(new
        {
            users = users.Select(u => new
            {
                id = u.Id,
                email = u.Email,
                displayName = u.DisplayName,
                role = u.Role.ToString().ToLowerInvariant(),
                status = u.Status.ToString().ToLowerInvariant(),
                isTenantAdmin = u.IsTenantAdmin,
                createdAt = u.CreatedAt,
                lastLoginAt = u.LastLoginAt
            })
        });
    }

    private static async Task<IResult> InviteUser(
        HttpContext context, IInvitationService invitationService,
        ICurrentPrincipal currentPrincipal, InviteUserRequest request, IValidator<InviteUserRequest> validator,
        CancellationToken ct = default) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var tc = context.GetTenantContext();
                var userId = currentPrincipal.RequireUserId();
                var result = await invitationService.InviteUserAsync(tc, userId, request.Email, request.Role, ct);
                if (result == null) throw new ArgumentException("User with this email already exists");
                return new
                {
                    invitation = new { id = result.Value.invitation.Id, email = result.Value.invitation.Email, role = result.Value.invitation.Role.ToString().ToLowerInvariant(), expiresAt = result.Value.invitation.ExpiresAt },
                    message = "Invitation sent successfully"
                };
            });

    private static async Task<IResult> UpdateUserRole(
        HttpContext context, IUserManagementService userManagementService,
        ICurrentPrincipal currentPrincipal, ILogger<EndpointLoggerCategory> logger, Guid userId,
        UpdateUserRoleRequest request, IValidator<UpdateUserRoleRequest> validator,
        CancellationToken ct = default) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var tc = context.GetTenantContext();
                var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
                var currentUserId = currentPrincipal.RequireUserId();
                if (userId == currentUserId) throw new ArgumentException("You cannot change your own role");
                var (success, error) = await userManagementService.UpdateUserRoleAsync(org, userId, request.Role, currentUserId, ct);
                if (error != null) return ErrorResponses.BadRequest(error);
                if (!success) throw new KeyNotFoundException("User not found");
                return Results.Ok(new { message = "User role updated successfully" });
            }, logger, "UpdateUserRole");

    private static async Task<IResult> DeleteUser(
        HttpContext context, IUserManagementService userManagementService,
        ICurrentPrincipal currentPrincipal, Guid userId,
        CancellationToken ct = default)
    {
        var tc = context.GetTenantContext();
        var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
        var currentUserId = currentPrincipal.RequireUserId();
        if (userId == currentUserId) throw new ArgumentException("You cannot delete your own account");
        var (success, error) = await userManagementService.DeleteUserAsync(org, userId, currentUserId, ct);
        if (error != null) return ErrorResponses.BadRequest(error);
        if (!success) throw new KeyNotFoundException("User not found");
        return Results.Ok(new { message = "User deleted successfully" });
    }

    private static async Task<IResult> GetPendingInvitations(HttpContext context, IInvitationService invitationService, CancellationToken ct = default)
    {
        var tc = context.GetTenantContext();
        var invitations = await invitationService.GetPendingInvitationsAsync(tc, ct) ?? [];
        return Results.Ok(new
        {
            invitations = invitations.Select(i => new
            {
                id = i.Id,
                email = i.Email,
                role = i.Role.ToString().ToLowerInvariant(),
                expiresAt = i.ExpiresAt,
                createdAt = i.CreatedAt
            })
        });
    }

    private static async Task<IResult> RevokeInvitation(
        HttpContext context, IInvitationService invitationService,
        ICurrentPrincipal currentPrincipal, Guid invitationId,
        CancellationToken ct = default)
    {
        var tc = context.GetTenantContext();
        var userId = currentPrincipal.RequireUserId();
        var success = await invitationService.RevokeInvitationAsync(tc, invitationId, userId, ct);
        if (!success) throw new KeyNotFoundException("Invitation not found or already accepted");
        return Results.Ok(new { message = "Invitation revoked successfully" });
    }
}
// InviteUserRequest, AcceptInvitationRequest, UpdateUserRoleRequest moved to Api.Models (Core)
