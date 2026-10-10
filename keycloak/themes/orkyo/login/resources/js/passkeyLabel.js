/**
 * Names a new passkey without asking. Keycloak's registration script asks with a browser
 * prompt after the passkey exists; no other site does that. The Orkyo copy of that script
 * (webauthnRegister.js, upstream except for two lines) calls this instead. The name can be
 * changed later on the Security tab.
 */

// Provider names for widely used passkey authenticators, keyed by AAGUID. Source: the
// community list at github.com/passkeydeveloper/passkey-authenticator-aaguids. AAGUIDs are
// public model identifiers, not secrets; each line carries gitleaks:allow because the
// secret scanner reads a quoted UUID as an API key.
export const PROVIDERS = {
  "fbfc3007-154e-4ecc-8c0b-6e020557d7bd": "iCloud Keychain", // gitleaks:allow
  "dd4ec289-e01d-41c9-bb89-70fa845d4bf2": "iCloud Keychain", // gitleaks:allow
  "ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4": "Google Password Manager", // gitleaks:allow
  "adce0002-35bc-c60a-648b-0b25f1f05503": "Chrome on Mac", // gitleaks:allow
  "08987058-cadc-4b81-b6e1-30de50dcbe96": "Windows Hello", // gitleaks:allow
  "9ddd1817-af5a-4672-a2b9-3e3dd95000a9": "Windows Hello", // gitleaks:allow
  "6028b017-b1d4-4c02-b4b3-afcdafc96bb2": "Windows Hello", // gitleaks:allow
  "bada5566-a7aa-401f-bd96-45619a55120d": "1Password", // gitleaks:allow
  "d548826e-79b4-db40-a3d8-11116f7e8349": "Bitwarden", // gitleaks:allow
  "531126d6-e717-415c-9320-3d9aa6981239": "Dashlane", // gitleaks:allow
  "53414d53-554e-4700-0000-000000000000": "Samsung Pass", // gitleaks:allow
  "fdb141b2-5d84-443e-8a35-4698c205a502": "KeePassXC", // gitleaks:allow
};

/**
 * The AAGUID from WebAuthn authenticator data, or null when the data carries no attested
 * credential: rpIdHash (32 bytes), flags (1), signCount (4), then the AAGUID (16) when the
 * AT flag (0x40) is set.
 */
export function aaguidOf(authenticatorData) {
  const bytes = new Uint8Array(authenticatorData);
  if (bytes.length < 53 || (bytes[32] & 0x40) === 0) return null;
  const hex = Array.from(bytes.slice(37, 53), (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** A short device name from the user agent; iOS before macOS and Android before Linux. */
export function deviceOf(userAgent) {
  if (/iPhone/.test(userAgent)) return "iPhone";
  if (/iPad/.test(userAgent)) return "iPad";
  if (/Android/.test(userAgent)) return "Android";
  if (/Windows/.test(userAgent)) return "Windows";
  if (/CrOS/.test(userAgent)) return "ChromeOS";
  if (/Macintosh|Mac OS X/.test(userAgent)) return "Mac";
  if (/Linux/.test(userAgent)) return "Linux";
  return null;
}

/**
 * The provider's name when the authenticator is a known one, otherwise "<fallback> on
 * <device>", otherwise the fallback. Never throws: a label must not stop a registration.
 */
export function passkeyLabel(credential, userAgent, fallback) {
  let aaguid = null;
  try {
    const data = credential?.response?.getAuthenticatorData?.();
    if (data) aaguid = aaguidOf(data);
  } catch {
    aaguid = null;
  }
  if (aaguid && PROVIDERS[aaguid]) return PROVIDERS[aaguid];
  const device = deviceOf(userAgent || "");
  return device ? `${fallback} on ${device}` : fallback;
}
