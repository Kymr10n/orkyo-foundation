namespace Api.Integrations.Keycloak;

/// <summary>
/// The Keycloak credential <c>type</c> values the account endpoints act on. Every removal and
/// rename names the type it expects, so a credential id of one kind (a password, say) cannot be
/// passed to the passkey or MFA route and act on another.
/// </summary>
public static class KeycloakCredentialTypes
{
    public const string Totp = "otp";
    public const string RecoveryCodes = "recovery-authn-codes";
    public const string Passkey = "webauthn-passwordless";
}
