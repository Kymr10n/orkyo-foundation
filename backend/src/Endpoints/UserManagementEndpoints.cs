using Api.Middleware;
using Api.Models;
using Api.Services;
using Api.Helpers;
using Api.Security;
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
            .WithTags("User Management");

        group.MapGet("/", GetAllUsers)
            .RequireRole(TenantRole.Admin)
            .WithName("GetAllUsers")
            .WithDescription("Get all users in the tenant (Admin only)");

        group.MapPost("/invite", InviteUser)
            .RequireRole(TenantRole.Admin)
            .WithName("InviteUser")
            .WithDescription("Invite a new user to the tenant (Admin only)")
            .Accepts<InviteUserRequest>("application/json");

        group.MapPatch("/{userId:guid}/role", UpdateUserRole)
            .RequireRole(TenantRole.Admin)
            .WithName("UpdateUserRole")
            .WithDescription("Update a user's role (Admin only)")
            .Accepts<UpdateUserRoleRequest>("application/json");

        group.MapDelete("/{userId:guid}", DeleteUser)
            .RequireRole(TenantRole.Admin)
            .WithName("DeleteUser")
            .WithDescription("Delete a user from the tenant (Admin only)");

        group.MapGet("/invitations", GetPendingInvitations)
            .RequireRole(TenantRole.Admin)
            .WithName("GetPendingInvitations")
            .WithDescription("Get all pending user invitations (Admin only)");

        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation)
            .RequireRole(TenantRole.Admin)
            .WithName("RevokeInvitation")
            .WithDescription("Revoke a pending invitation (Admin only)");

        // Public invitation endpoints (no auth, no tenant)
        app.MapGet("/api/invitations/validate", async (IInvitationService invitationService, [FromQuery] string token) =>
        {
            var (email, expiresAt, tenantName, error) = await invitationService.ValidateInvitationAsync(token);
            return error != null
                ? Results.BadRequest(new { error })
                : Results.Ok(new { email, expiresAt, tenantName });
        })
        .AllowAnonymous()
        .WithMetadata(new SkipTenantResolutionAttribute())
        .WithTags("User Management")
        .WithName("ValidateInvitation")
        .WithDescription("Validate an invitation token and get invitation details");

        app.MapPost("/api/invitations/accept", async (IInvitationService invitationService, AcceptInvitationRequest request, IValidator<AcceptInvitationRequest> validator) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var (user, error) = await invitationService.AcceptInvitationAsync(request.Token, request.DisplayName, request.Password);
                if (error != null) throw new ArgumentException(error);
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

    private static async Task<IResult> GetAllUsers(HttpContext context, IUserManagementService userManagementService)
    {
        var tc = context.GetTenantContext();
        var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
        var users = await userManagementService.GetAllUsersAsync(org);
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
        ICurrentPrincipal currentPrincipal, InviteUserRequest request, IValidator<InviteUserRequest> validator) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var tc = context.GetTenantContext();
                var userId = currentPrincipal.RequireUserId();
                var result = await invitationService.InviteUserAsync(tc, userId, request.Email, request.Role);
                if (result == null) throw new ArgumentException("User with this email already exists");
                return new
                {
                    invitation = new { id = result.Value.invitation.Id, email = result.Value.invitation.Email, role = result.Value.invitation.Role.ToString().ToLowerInvariant(), expiresAt = result.Value.invitation.ExpiresAt },
                    message = "Invitation sent successfully"
                };
            });

    private static async Task<IResult> UpdateUserRole(
        HttpContext context, IUserManagementService userManagementService,
        ICurrentPrincipal currentPrincipal, Guid userId, UpdateUserRoleRequest request, IValidator<UpdateUserRoleRequest> validator) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var tc = context.GetTenantContext();
                var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
                var currentUserId = currentPrincipal.RequireUserId();
                if (userId == currentUserId) throw new ArgumentException("You cannot change your own role");
                var (success, error) = await userManagementService.UpdateUserRoleAsync(org, userId, request.Role, currentUserId);
                if (error != null) throw new InvalidOperationException(error);
                if (!success) throw new KeyNotFoundException("User not found");
                return new { message = "User role updated successfully" };
            });

    private static async Task<IResult> DeleteUser(
        HttpContext context, IUserManagementService userManagementService,
        ICurrentPrincipal currentPrincipal, ILogger<EndpointLoggerCategory> logger, Guid userId) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var tc = context.GetTenantContext();
                var org = new OrgContext { OrgId = tc.TenantId, OrgSlug = tc.TenantSlug, DbConnectionString = tc.TenantDbConnectionString };
                var currentUserId = currentPrincipal.RequireUserId();
                if (userId == currentUserId) throw new ArgumentException("You cannot delete your own account");
                var (success, error) = await userManagementService.DeleteUserAsync(org, userId, currentUserId);
                if (error != null) throw new InvalidOperationException(error);
                if (!success) throw new KeyNotFoundException("User not found");
                return Results.Ok(new { message = "User deleted successfully" });
            }, logger, "DeleteUser");

    private static async Task<IResult> GetPendingInvitations(HttpContext context, IInvitationService invitationService)
    {
        var tc = context.GetTenantContext();
        var invitations = await invitationService.GetPendingInvitationsAsync(tc);
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
        ICurrentPrincipal currentPrincipal, ILogger<EndpointLoggerCategory> logger, Guid invitationId) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var tc = context.GetTenantContext();
                var userId = currentPrincipal.RequireUserId();
                var success = await invitationService.RevokeInvitationAsync(tc, invitationId, userId);
                if (!success) throw new KeyNotFoundException("Invitation not found or already accepted");
                return Results.Ok(new { message = "Invitation revoked successfully" });
            }, logger, "RevokeInvitation");
}

public record InviteUserRequest(string Email, UserRole Role);
public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
public record UpdateUserRoleRequest(UserRole Role);
