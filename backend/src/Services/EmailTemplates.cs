using Api.Models;
using Orkyo.Shared;

namespace Api.Services;

public record EmailBranding(
    string ProductName,
    string PrimaryColor,
    string SecondaryColor)
{
    /// <summary>Fallback instance using compiled defaults from <see cref="TenantSettings"/>.</summary>
    public static EmailBranding Default { get; } = new TenantSettings().ToEmailBranding();
}

public static class EmailTemplates
{
    private static EmailBranding Resolve(EmailBranding? b) => b ?? EmailBranding.Default;

    public static (string subject, string htmlBody, string textBody) GetVerificationEmail(
        string displayName, string verificationLink, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = "Verify your email address";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Verify Your Email</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">Welcome to {b.ProductName}!</h1>
    </div>
    
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hi {displayName},</p>
        
        <p style=""font-size: 16px; margin-bottom: 20px;"">
            Thank you for registering with {b.ProductName}. To complete your registration and activate your account, 
            please verify your email address by clicking the button below.
        </p>
        
        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{verificationLink}"" 
               style=""background-color: {b.PrimaryColor}; color: white; padding: 14px 30px; text-decoration: none; 
                      border-radius: 5px; font-size: 16px; font-weight: bold; display: inline-block;"">
                Verify Email Address
            </a>
        </div>
        
        <p style=""font-size: 14px; color: #666; margin-top: 30px;"">
            If the button doesn't work, copy and paste this link into your browser:
        </p>
        <p style=""font-size: 14px; color: {b.PrimaryColor}; word-break: break-all;"">
            {verificationLink}
        </p>
        
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        
        <p style=""font-size: 13px; color: #999; margin-top: 20px;"">
            This verification link will expire in 7 days. If you didn't create an account with us, 
            you can safely ignore this email.
        </p>
        
        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"Welcome to {b.ProductName}!

Hi {displayName},

Thank you for registering with {b.ProductName}. To complete your registration and activate your account, please verify your email address by visiting the link below:

{verificationLink}

This verification link will expire in 7 days. If you didn't create an account with us, you can safely ignore this email.

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetPasswordResetEmail(
        string displayName, string resetLink, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = "Reset your password";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reset Your Password</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">Password Reset Request</h1>
    </div>
    
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hi {displayName},</p>
        
        <p style=""font-size: 16px; margin-bottom: 20px;"">
            We received a request to reset your password for your {b.ProductName} account. 
            Click the button below to create a new password.
        </p>
        
        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{resetLink}"" 
               style=""background-color: {b.PrimaryColor}; color: white; padding: 14px 30px; text-decoration: none; 
                      border-radius: 5px; font-size: 16px; font-weight: bold; display: inline-block;"">
                Reset Password
            </a>
        </div>
        
        <p style=""font-size: 14px; color: #666; margin-top: 30px;"">
            If the button doesn't work, copy and paste this link into your browser:
        </p>
        <p style=""font-size: 14px; color: {b.PrimaryColor}; word-break: break-all;"">
            {resetLink}
        </p>
        
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        
        <p style=""font-size: 13px; color: #999; margin-top: 20px;"">
            This password reset link will expire in 1 hour. If you didn't request a password reset, 
            you can safely ignore this email. Your password will remain unchanged.
        </p>
        
        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"Password Reset Request

Hi {displayName},

We received a request to reset your password for your {b.ProductName} account. Visit the link below to create a new password:

{resetLink}

This password reset link will expire in 1 hour. If you didn't request a password reset, you can safely ignore this email. Your password will remain unchanged.

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetWelcomeEmail(
        string displayName, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = $"Welcome to {b.ProductName}!";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Welcome!</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">🎉 Welcome Aboard!</h1>
    </div>
    
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hi {displayName},</p>
        
        <p style=""font-size: 16px; margin-bottom: 20px;"">
            Your email has been verified successfully! You're now ready to start using {b.ProductName} 
            to manage your resources efficiently.
        </p>
        
        <h2 style=""color: {b.PrimaryColor}; font-size: 20px; margin-top: 30px;"">Getting Started</h2>
        
        <ul style=""font-size: 16px; line-height: 2;"">
            <li>Create your first site and spaces</li>
            <li>Set up resource utilization schedules</li>
            <li>Invite team members to collaborate</li>
            <li>Track and optimize your resource utilization</li>
        </ul>
        
        <p style=""font-size: 16px; margin-top: 30px;"">
            If you have any questions or need help getting started, feel free to reach out to our support team.
        </p>
        
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        
        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"Welcome Aboard!

Hi {displayName},

Your email has been verified successfully! You're now ready to start using {b.ProductName} to manage your resources efficiently.

Getting Started:
- Create your first site and spaces
- Set up resource utilization schedules
- Invite team members to collaborate
- Track and optimize your resource utilization

If you have any questions or need help getting started, feel free to reach out to our support team.

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetLifecycleWarningEmail(
        string displayName, string confirmLink, int warningNumber, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = warningNumber == 1
            ? $"Is your {b.ProductName} account still active?"
            : $"Reminder: confirm your {b.ProductName} account activity ({warningNumber}/3)";

        var urgency = warningNumber switch
        {
            1 => "We noticed you haven't logged in for a while.",
            2 => "This is a reminder — we still haven't heard from you.",
            _ => "This is your final reminder before your account is deactivated."
        };

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Account Activity Check</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">Account Activity Check</h1>
    </div>

    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hi {displayName},</p>

        <p style=""font-size: 16px; margin-bottom: 20px;"">
            {urgency} To keep your {b.ProductName} account active, please confirm that you are still using it.
        </p>

        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{confirmLink}""
               style=""background-color: {b.PrimaryColor}; color: white; padding: 14px 30px; text-decoration: none;
                      border-radius: 5px; font-size: 16px; font-weight: bold; display: inline-block;"">
                Yes, keep my account active
            </a>
        </div>

        <p style=""font-size: 14px; color: #666; margin-top: 30px;"">
            If the button doesn't work, copy and paste this link into your browser:
        </p>
        <p style=""font-size: 14px; color: {b.PrimaryColor}; word-break: break-all;"">
            {confirmLink}
        </p>

        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">

        <p style=""font-size: 13px; color: #999; margin-top: 20px;"">
            If you do not confirm within the next two weeks, we will send another reminder.
            After three reminders without a response, your account will be temporarily deactivated
            and permanently deleted after {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days, in line with our data retention policy.
        </p>
        <p style=""font-size: 13px; color: #999;"">
            If you no longer wish to use {b.ProductName}, you can simply ignore this email.
        </p>

        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"Account Activity Check — {b.ProductName}

Hi {displayName},

{urgency} To keep your {b.ProductName} account active, please confirm that you are still using it by visiting the link below:

{confirmLink}

If you do not confirm within the next two weeks, we will send another reminder. After three reminders without a response, your account will be temporarily deactivated and permanently deleted after {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days.

If you no longer wish to use {b.ProductName}, you can simply ignore this email.

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetDormancyNoticeEmail(
        string displayName, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = $"Your {b.ProductName} account has been deactivated";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Account Deactivated</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, #e67e22 0%, #c0392b 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">Account Deactivated</h1>
    </div>

    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hi {displayName},</p>

        <p style=""font-size: 16px; margin-bottom: 20px;"">
            We sent you three reminders about your {b.ProductName} account inactivity and received no response,
            so your account has now been <strong>deactivated</strong>.
        </p>

        <p style=""font-size: 16px; margin-bottom: 20px;"">
            Your account and data will be <strong>permanently deleted in {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days</strong>.
            If you would like to reactivate your account before then, please contact our support team.
        </p>

        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">

        <p style=""font-size: 13px; color: #999; margin-top: 20px;"">
            This action was taken in accordance with our data retention and privacy policy (GDPR Article 5).
        </p>

        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"Account Deactivated — {b.ProductName}

Hi {displayName},

We sent you three reminders about your {b.ProductName} account inactivity and received no response, so your account has now been deactivated.

Your account and data will be permanently deleted in {LifecyclePolicyConstants.UserPurgeAfterDormantDays} days. If you would like to reactivate your account before then, please contact our support team.

This action was taken in accordance with our data retention and privacy policy (GDPR Article 5).

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetNewUserAlertEmail(
        string userEmail, string displayName, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = $"[{b.ProductName}] New user registered: {userEmail}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>New User Registration</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 24px;"">New User Registered</h1>
    </div>
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px;"">A new user has registered on <strong>{b.ProductName}</strong>.</p>
        <table style=""width: 100%; border-collapse: collapse; margin-top: 16px;"">
            <tr>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0; font-weight: bold; width: 40%;"">Email</td>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0;"">{userEmail}</td>
            </tr>
            <tr>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0; font-weight: bold;"">Display Name</td>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0;"">{displayName}</td>
            </tr>
            <tr>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0; font-weight: bold;"">Registered At</td>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0;"">{timestamp}</td>
            </tr>
        </table>
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        <p style=""font-size: 13px; color: #999;"">This is an automated alert from {b.ProductName}.</p>
    </div>
</body>
</html>";

        var textBody = $@"New User Registered — {b.ProductName}

A new user has registered on {b.ProductName}.

Email:        {userEmail}
Display Name: {displayName}
Registered:   {timestamp}

This is an automated alert from {b.ProductName}.";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetNewTenantAlertEmail(
        string tenantSlug, string tenantDisplayName, string ownerEmail, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = $"[{b.ProductName}] New tenant created: {tenantSlug}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>New Tenant Created</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 24px;"">New Tenant Created</h1>
    </div>
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px;"">A new tenant has been created on <strong>{b.ProductName}</strong>.</p>
        <table style=""width: 100%; border-collapse: collapse; margin-top: 16px;"">
            <tr>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0; font-weight: bold; width: 40%;"">Slug</td>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0;"">{tenantSlug}</td>
            </tr>
            <tr>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0; font-weight: bold;"">Display Name</td>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0;"">{tenantDisplayName}</td>
            </tr>
            <tr>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0; font-weight: bold;"">Owner Email</td>
                <td style=""padding: 8px 12px; background: #fff; border: 1px solid #e0e0e0;"">{ownerEmail}</td>
            </tr>
            <tr>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0; font-weight: bold;"">Created At</td>
                <td style=""padding: 8px 12px; background: #f5f5f5; border: 1px solid #e0e0e0;"">{timestamp}</td>
            </tr>
        </table>
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        <p style=""font-size: 13px; color: #999;"">This is an automated alert from {b.ProductName}.</p>
    </div>
</body>
</html>";

        var textBody = $@"New Tenant Created — {b.ProductName}

A new tenant has been created on {b.ProductName}.

Slug:         {tenantSlug}
Display Name: {tenantDisplayName}
Owner Email:  {ownerEmail}
Created:      {timestamp}

This is an automated alert from {b.ProductName}.";

        return (subject, htmlBody, textBody);
    }

    public static (string subject, string htmlBody, string textBody) GetInvitationEmail(
        string signupLink, DateTime expiresAt, EmailBranding? branding = null)
    {
        var b = Resolve(branding);
        var subject = $"You've been invited to join {b.ProductName}";
        var expiryText = expiresAt.ToString("MMMM dd, yyyy 'at' HH:mm UTC");

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Invitation to Join</title>
</head>
<body style=""font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: linear-gradient(135deg, {b.PrimaryColor} 0%, {b.SecondaryColor} 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;"">
        <h1 style=""color: white; margin: 0; font-size: 28px;"">🎉 You're Invited!</h1>
    </div>
    
    <div style=""background-color: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px;"">
        <p style=""font-size: 16px; margin-bottom: 20px;"">Hello,</p>
        
        <p style=""font-size: 16px; margin-bottom: 20px;"">
            You've been invited to join a {b.ProductName} workspace. Click the button below 
            to accept the invitation and create your account.
        </p>
        
        <div style=""text-align: center; margin: 30px 0;"">
            <a href=""{signupLink}"" 
               style=""background-color: {b.PrimaryColor}; color: white; padding: 14px 30px; text-decoration: none; 
                      border-radius: 5px; font-size: 16px; font-weight: bold; display: inline-block;"">
                Accept Invitation
            </a>
        </div>
        
        <p style=""font-size: 14px; color: #666; margin-top: 30px;"">
            If the button doesn't work, copy and paste this link into your browser:
        </p>
        <p style=""font-size: 14px; color: {b.PrimaryColor}; word-break: break-all;"">
            {signupLink}
        </p>
        
        <hr style=""border: none; border-top: 1px solid #ddd; margin: 30px 0;"">
        
        <p style=""font-size: 13px; color: #999; margin-top: 20px;"">
            This invitation link will expire on {expiryText}. If you didn't expect this invitation, 
            you can safely ignore this email.
        </p>
        
        <p style=""font-size: 13px; color: #999; margin-top: 10px;"">
            Best regards,<br>
            The {b.ProductName} Team
        </p>
    </div>
</body>
</html>";

        var textBody = $@"You're Invited to {b.ProductName}!

Hello,

You've been invited to join a {b.ProductName} workspace. Visit the link below to accept the invitation and create your account:

{signupLink}

This invitation link will expire on {expiryText}. If you didn't expect this invitation, you can safely ignore this email.

Best regards,
The {b.ProductName} Team";

        return (subject, htmlBody, textBody);
    }
}
