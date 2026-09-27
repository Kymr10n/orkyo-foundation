# Passkeys — specification and implementation plan

Status: **proposed**, written 2026-09-27 on branch `claude/orkyo-qr-resource-linking-u77if7`.
Nothing in this document is implemented yet.

This document adds passkeys as a sign-in method for Orkyo. A passkey is a WebAuthn
credential that a device or a password manager stores. The user signs in with a fingerprint,
a face, or a device PIN. No password travels over the network.

Three product decisions apply:

1. Passkeys are an addition. Password plus TOTP stays available. No user is locked out
   because a device is lost.
2. Keycloak is the relying party. Orkyo does not implement WebAuthn itself.
3. One passkey serves the apex and every tenant host. The relying-party ID is the base
   domain.

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
| Relying party ID | base domain (`orkyo.com`, or the Community host) | One passkey for the apex and every tenant host. The session cookie already spans this domain. |
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
rebuilds the flow from the current built-in flow, then applies the same two changes. The
rebuilt flow gains the **Condition - credential** execution in the 2FA sub-flow. This
condition skips the sub-flow after a passkey login.

Community uses the built-in `browser` flow. Keycloak updates that flow itself.

### 3.3 Login theme

The custom `login.ftl` gets the same username input as the base template, with
`autocomplete="username webauthn"`. It also gets the passkey button block. The base template
renders that block when the realm has passkeys enabled. Two new templates take the Orkyo look:
`webauthn-authenticate.ftl` and `webauthn-register.ftl`. Both inherit `template.ftl` and
keep the base JavaScript unchanged.

The password form keeps its **Sign in with Passkey** button for users that typed a username
first.

## 4. API

All routes sit in the existing account group under `/api/account`. The group already
requires an authenticated user and applies the account mutation guard, which locks the demo
account.

| Route | Purpose | Notes |
|---|---|---|
| `GET /api/account/passkeys` | Lists the user's passwordless WebAuthn credentials. | Reads Keycloak credentials of type `webauthn-passwordless`. Returns id, label, created date. |
| `POST /api/account/passkeys/enrol` | Starts enrolment. | Adds the `webauthn-register-passwordless` required action to the Keycloak user. Returns the BFF login URL with `kc_action=webauthn-register-passwordless`, so the browser goes to Keycloak now, not at the next login. |
| `DELETE /api/account/passkeys/{credentialId}` | Removes one passkey. | Refuses when the credential is not a passwordless WebAuthn credential of this user. |
| `PATCH /api/account/passkeys/{credentialId}` | Renames a passkey. | Sets the credential label in Keycloak. |

The BFF login endpoint accepts a `kc_action` query parameter and forwards it to Keycloak.
Only values from an allow-list pass: `webauthn-register-passwordless` and, later,
`CONFIGURE_TOTP`. This is the Keycloak "application initiated action" mechanism. After the
action Keycloak returns to the BFF callback as after a normal login.

`KeycloakAdminService` gains three methods: list credentials of a user, set a credential
label, and add a required action by name. `EnableMfaAsync` then becomes a call of the last
one. `KeycloakRequiredActions` gains `WebAuthnRegisterPasswordless`.

Both enrolment and removal send the existing security-changed email and write an audit
entry with the credential label.

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
- Enrolment requires a fresh session. The BFF login with `kc_action` re-authenticates the
  user at Keycloak before the required action runs. This is the Keycloak default for
  application-initiated actions.
- The `kc_action` allow-list prevents an open redirect into arbitrary required actions.
- Recovery codes stay the break-glass path for TOTP. They do not apply to passkey logins.
- Audit entries record enrolment and removal, with the credential label and the actor.

## 7. Documentation

`orkyo-documentation` `user-guide/account-and-security.md` gets a **Passkeys** subsection
under **Security**, in the same descriptive style as the TOTP one, with the UI strings quoted
verbatim. The 0.28.0 release note announces the feature.

## 8. Rollout

1. Ship opt-in. The Security page shows the card. Nothing prompts the user.
2. After one release cycle, add a one-time notice after login for users with no passkey.
   The notice links to the Security page and has a dismiss action.
3. Site admins: after every admin has two passkeys, one of them synced, consider a realm
   policy that requires a passkey for the site-admin role. This is a separate decision.

## 9. Decisions and open questions

Decided:

- Relying-party ID is the base domain. One passkey for all tenant hosts.
- Passkeys replace the password step, not the second factor.
- Enrolment goes through the app, with the Keycloak account console as a fallback.

Open:

- **Community relying-party ID.** Self-hosters set `APP_BASE_URL`. The policy takes its host.
  The realm export cannot carry a per-installation value, so the entrypoint must set it at
  start. This needs a small change in `backend/keycloak/docker-entrypoint.sh`.
- **Rebuild of `orkyo-browser`.** A rebuilt flow gets a new ID. The configure script must
  set the realm's browser flow to the new one and delete the old one in one deploy.
- **Theme templates.** The base `webauthn-register.ftl` loads a script from the theme
  resources. The Orkyo theme must keep that script path valid.

## 10. Implementation plan

### Phase 1 — Keycloak (orkyo-infra, orkyo-community)

- Configure script: WebAuthn Passwordless policy, rebuilt `orkyo-browser` flow, verification
  step that the realm reports `passkeys` as enabled.
- Community: policy values in `realm.json`, relying-party ID from `APP_BASE_URL` in the
  entrypoint.
- Verify on staging: passkey sign-in on iOS Safari, Android Chrome, and a desktop browser.
  Password plus TOTP unchanged.

### Phase 2 — theme (orkyo-foundation)

- `login.ftl`: username input with `webauthn` autofill and the passkey button block.
- New `webauthn-authenticate.ftl` and `webauthn-register.ftl` in the Orkyo look.
- The Keycloak dry build in CI covers the templates.

### Phase 3 — backend and frontend (orkyo-foundation)

- `KeycloakAdminService`: list credentials, set label, add required action.
- BFF login: `kc_action` allow-list.
- Security endpoints: the four routes of section 4, with tests for refusal paths.
- Frontend: `PasskeysSection` with hooks, tests, and the demo lock.
- Coverage at or above 80 percent for the patch, as for every change.

### Phase 4 — documentation and release

- User guide subsection, release note, marketing mention.
- Release as 0.28.0. The realm changes deploy first, the app changes ride the same train.
