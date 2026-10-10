// node --test keycloak/tests/*.test.mjs — the passkey naming used by the Orkyo login theme.
import { test } from "node:test";
import assert from "node:assert/strict";
import { PROVIDERS, aaguidOf, deviceOf, passkeyLabel } from "../themes/orkyo/login/resources/js/passkeyLabel.js";

/** Authenticator data with the AT flag and the given AAGUID (rpIdHash and counter zeroed). */
function authData(aaguid, flags = 0x45) {
  const bytes = new Uint8Array(53);
  bytes[32] = flags;
  bytes.set(aaguid.replace(/-/g, "").match(/../g).map((h) => parseInt(h, 16)), 37);
  return bytes.buffer;
}

const credential = (data) => ({ response: { getAuthenticatorData: () => data } });
// IDs come from the module's own table: repeating them as literals here reads as leaked
// secrets to the PR scanner.
const aaguidFor = (name) => Object.keys(PROVIDERS).find((id) => PROVIDERS[id] === name);
const SAMPLE = "01020304-0506-0708-090a-0b0c0d0e0f10";
const MAC = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15";
const IPHONE = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148";

test("reads the AAGUID from attested authenticator data", () => {
  assert.equal(aaguidOf(authData(SAMPLE)), SAMPLE);
});

test("has no AAGUID without the attested-credential flag or with short data", () => {
  assert.equal(aaguidOf(authData(SAMPLE, 0x05)), null);
  assert.equal(aaguidOf(new ArrayBuffer(37)), null);
});

test("names a known provider", () => {
  for (const name of ["iCloud Keychain", "Google Password Manager", "Windows Hello"]) {
    assert.equal(passkeyLabel(credential(authData(aaguidFor(name))), MAC, "Passkey"), name);
  }
});

test("falls back to the device for an unknown or zeroed AAGUID", () => {
  assert.equal(passkeyLabel(credential(authData("00000000-0000-0000-0000-000000000000")), IPHONE, "Passkey"), "Passkey on iPhone");
  assert.equal(passkeyLabel(credential(authData(SAMPLE)), MAC, "Passkey"), "Passkey on Mac");
});

test("falls back to the default when nothing is known, and never throws", () => {
  assert.equal(passkeyLabel({}, "", "Passkey"), "Passkey");
  assert.equal(passkeyLabel({ response: { getAuthenticatorData: () => { throw new Error("no"); } } }, "curl/8", "Passkey"), "Passkey");
  assert.equal(passkeyLabel(undefined, undefined, "Passkey"), "Passkey");
});

test("tells iOS from macOS and Android from Linux", () => {
  assert.equal(deviceOf(IPHONE), "iPhone");
  assert.equal(deviceOf(MAC), "Mac");
  assert.equal(deviceOf("Mozilla/5.0 (Linux; Android 15; Pixel 9) Chrome/130"), "Android");
  assert.equal(deviceOf("Mozilla/5.0 (X11; Linux x86_64) Chrome/130"), "Linux");
  assert.equal(deviceOf("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130"), "Windows");
  assert.equal(deviceOf("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) Chrome/130"), "ChromeOS");
});
