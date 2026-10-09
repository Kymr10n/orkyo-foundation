# Passkeys — specification and implementation plan

Status: **in progress**. Written 2026-09-27, revised 2026-10-09 for the v1 implementation.
Done: the login theme (0.28), the Community realm and converger (1.5.0). In review: the hosted
Keycloak configuration (orkyo-infra) and the account API, BFF and Security card (this repo).

This document adds passkeys as a sign-in method for Orkyo. A passkey is a WebAuthn
credential that a device or a password manager stores. The user signs in with a fingerprint,
a face, or a device PIN. No password travels over the network.

Three product decisions apply:

1. Passkeys are an addition. Password plus TOTP stays available. No user is locked out
   because a device is lost.
2. Keycloak is the relying party. Orkyo does not implement WebAuthn itself.
3. One passkey serves the apex and every tenant host. Every passkey ceremony runs on the
   Keycloak host, so that host is the relying-party ID.

## 1. Evaluation

Keycloak is the identity provider of both editions. The pinned image is Keycloak 26.7.3.
This version ships the `passkeys` feature as a default feature. The feature adds two things
to the standard login forms. The username field offers passkeys through browser autofill.
A **Sign in with Passkey** button opens the platform dialog. The default browser flow also
skips the second factor when the user signed in with a passkey.

The current setup has four gaps:

| Gap | Where | Effect today |
|---|---|---|
| No WebAuthn Passwordless policy | Realm export and configure script | Defaults apply. The relying-party ID is the auth host, not the base domain. |
| Custom browser flow predates passkeys | `scripts/configure-keycloak-deploy.sh` (infra) | The `orkyo-browser` flow has no credential condition. A passkey login still asks for TOTP. |
| Custom login template | `keycloak/themes/orkyo/login/login.ftl` | The username input has `autocomplete="username"`. Browser autofill of passkeys does not appear. |
| No passkey management in the app | `SecuritySettings` in the frontend | Users cannot add or remove a passkey without the Keycloak account console. |

The `webauthn-register-passwordless` required action is already enabled in both realm
exports. The backend already adds a required action to a user for TOTP enrolment. The same
mechanism serves passkey enrolment.

## 2. Scope

In scope:

- Realm policy, browser flow, and login theme for passkey sign-in and enrolment.
- A **Passkeys** section on the Security page: list, add, remove.
- The same behaviour in SaaS and Community.

Out of scope:

- Passkeys as a second factor for password logins. The standard WebAuthn policy stays as it
  is. TOTP remains the second factor.
- Mandatory passkeys for any role. Section 8 describes a later step for site admins.
- Passkeys for the Google identity provider. Those accounts sign in at Google.
- The shared demo account. It stays locked for all security settings.

## 3. Keycloak configuration

### 3.1 WebAuthn Passwordless policy

The configure script sets the policy on every deploy, in the same idempotent style as the
other realm settings. Community carries the same values in `realm.json`.

| Setting | Value | Reason |
|---|---|---|
| Relying party entity name | `Orkyo` | Shown by the platform dialog. |
| Relying party ID | hosted: the Keycloak host (`KEYCLOAK_HOSTNAME`); Community: the `APP_BASE_URL` host | Every ceremony runs on the Keycloak host, so one passkey covers the apex and every tenant host. Staging and production keep separate IDs. |
| Signature algorithms | ES256, RS256 | Broad device support. |
| Attestation conveyance | none | Orkyo does not verify authenticator makes. |
| Authenticator attachment | not specified | Platform and roaming authenticators both work. |
| Require discoverable credential | yes | Required for username-less sign-in. |
| User verification requirement | required | The device must verify the user. |
| Passkey mediation | conditional | Autofill only. No dialog on page load. |
| Create timeout | 60 s | Default. |
| Avoid same authenticator registration | no | A user can register the same authenticator twice. |

The relying-party ID cannot change after users enrol. Existing passkeys stop working when it
changes. Section 9 records the decision.

### 3.2 Browser flow

The `orkyo-browser` flow is a copy of the built-in browser flow with two changes. Recovery
codes are an alternative to TOTP, and the OTP form is an alternative step. The configure script
does not rebuild the flow. It adds the **Condition - credential** execution to the existing
2FA sub-flow, sets it to Required with `credentials=webauthn-passwordless`, and moves it
directly after **Condition - user configured**. This condition skips the sub-flow after a
passkey login. Every write follows a read, so a converged realm gets no write.

Community's converger (`release/scripts/keycloak-converge.py`) applies the same step to the
bound flow on every start.

### 3.3 Login theme

The custom `login.ftl` gets the same username input as the base template, with
`autocomplete="username webauthn"`. It also gets the passkey button block. The base template
renders that block when the realm has passkeys enabled. `webauthn-authenticate.ftl` and
`webauthn-register.ftl` come from the base theme unchanged. `theme.properties` maps their
classes to the Orkyo styles, so their JavaScript and script paths stay the upstream ones.

The password form keeps its **Sign in with Passkey** button for users that typed a username
first.

## 4. API

All routes sit in the existing account group under `/api/account`. The group already
requires an authenticated user and applies the account mutation guard, which locks the demo
account.

| Route | Purpose | Notes |
|---|---|---|
| `GET /api/account/passkeys` | Lists the user's passkeys. | Reads Keycloak credentials of type `webauthn-passwordless`, oldest first. Returns id, label, created date. |
| `PATCH /api/account/passkeys/{credentialId}` | Renames a passkey. | Sets the credential label in Keycloak. Refuses a credential of another user with 404. |
| `DELETE /api/account/passkeys/{credentialId}` | Removes one passkey. | Re-checks the current password, and the TOTP code for a TOTP user, as removing MFA does. Refuses a credential of another user with 404. Sends a "passkey removed" email. |

Enrolment has no route. The **Add a passkey** button sends the browser to the BFF login with
`kc_action=webauthn-register-passwordless`. The BFF forwards the parameter only from an
allow-list with that one value. This is Keycloak's application-initiated action: the user
can cancel it, which a required action set through the admin API does not allow. Keycloak
returns to the BFF callback with `kc_action_status` (`success`, `cancelled` or `error`). The
callback appends that status to `returnTo` and skips the landing-page choice of a plain
sign-in, so the browser comes back to the Security tab. The account page turns the status
into a toast.

`KeycloakAdminService` gains two methods: list the passkeys of a user, and set a credential
label. Removal uses the existing `DeleteUserCredentialAsync`. No required action and no
`KeycloakRequiredActions` entry are needed.

## 5. User interface

### 5.1 Passkeys section

The Security page gets a **Passkeys** card between **Password** and **Two-Factor
Authentication (MFA)**. The card shows one row per passkey: label, created date, and a
**Remove** action with a confirmation dialog. **Add a passkey** starts enrolment. The
browser goes to Keycloak, the platform dialog registers the passkey, and the browser returns
to the Security page. A toast confirms the new passkey.

With no passkey the card explains the benefit in one sentence: "Sign in with your
fingerprint, face, or device PIN. No password to type."

The card follows the dialog and mutation rules of `docs/dialog-feedback.md`. The demo
account sees the same lock notice as the other cards.

### 5.2 Login

The Keycloak login page is the only sign-in surface. With passkeys enabled, the username
field offers stored passkeys through autofill, and the **Sign in with Passkey** button
opens the platform dialog. After a passkey login, Keycloak skips TOTP. After a password
login, the flow stays as today.

The BFF `login_hint` continues to prefill the email.

## 6. Security

- User verification is required. A passkey login proves possession and presence.
- A passkey login skips TOTP. This is the standard Keycloak behaviour and follows the FIDO
  guidance: the authenticator already verified the user.
- Removal of the last passkey is allowed. The password stays as a sign-in method, so the
  user is never locked out. Removal of the password is not possible today.
- Enrolment requires a recent sign-in. Keycloak asks for the password again when the session
  is older than the action's maximum authentication age. This is the Keycloak default for
  application-initiated actions.
- The `kc_action` allow-list prevents an open redirect into arbitrary required actions.
- Recovery codes stay the break-glass path for TOTP. They do not apply to passkey logins.
- No audit entries yet: the security endpoints write none today, for MFA either. Audit
  entries and a "passkey added" email are follow-ups. The added email needs a Keycloak event,
  because the backend does not see the enrolment.

## 7. Documentation

`orkyo-documentation` `user-guide/account-and-security.md` gets a **Passkeys** subsection
under **Security**, in the same descriptive style as the TOTP one, with the UI strings quoted
verbatim. The release note of the release that ships the card announces the feature.

## 8. Rollout

1. Ship opt-in. The Security page shows the card. Nothing prompts the user.
2. After one release cycle, add a one-time notice after login for users with no passkey.
   The notice links to the Security page and has a dismiss action.
3. Site admins: after every admin has two passkeys, one of them synced, consider a realm
   policy that requires a passkey for the site-admin role. This is a separate decision.

## 9. Decisions and open questions

Decided:

- Relying-party ID is the Keycloak host on the hosted service (decided 2026-10-09). The base
  domain would let staging and production share passkeys.
- Community relying-party ID is the `APP_BASE_URL` host. The entrypoint sets it on first
  import and the converger on every start (1.5.0).
- Passkeys replace the password step, not the second factor.
- Enrolment goes through the app, with the Keycloak account console as a fallback.
- The configure script edits the existing `orkyo-browser` flow. It does not rebuild it.
- Passkeys need HTTPS. A Community installation on plain HTTP gets none; the converger warns.

Open: none for v1.

## 10. Implementation plan

v1 ships in five pull requests:

1. orkyo-infra: the passwordless policy with the Keycloak host as ID, the
   **Condition - credential** step in `orkyo-browser`, the required action enabled, a smoke
   check for `username webauthn` on the login page, and the MFA documentation.
2. orkyo-foundation: the three routes of section 4, the BFF `kc_action` allow-list and
   `kc_action_status` passthrough, the Passkeys card on the Security tab, and this document.
3. orkyo-saas and orkyo-community: the foundation pin bump.
4. orkyo-community: the converger warns on an `http://` `APP_BASE_URL`.
5. orkyo-documentation: a **Passkeys** subsection in the user guide and the HTTPS note for
   Community operators.

The realm change goes to staging first. Production gets it in the same release train as the
card, so the passkey UI never appears on the production login page before the card exists.

Follow-ups: a one-time notice for users with no passkey, a passkey requirement for site
admins, an end-to-end test with a virtual authenticator, audit entries, and a "passkey added"
email.
