#!/usr/bin/env python3
"""check-realm-contract.py — every Keycloak realm export in the four repos keeps the contract
the application code depends on, and the two orkyo-saas local copies differ only where they
are meant to.

WHY THIS EXISTS
The realm is defined five times (saas#307): production/staging in orkyo-infra, the Community
self-host bundle, Community local, and two orkyo-saas local copies. The names the code reads
(realm roles, the backend client id, the login theme) are string literals in JSON, and nothing
compared the files. The saas pair is meant to differ in exactly two ways, the redirect URLs of
the orkyo.local stack and one documented admin fixture, and had no check that it stays so.

Runs in template-sync-check with the sibling checkouts present. A realm file that is absent
is skipped with a note, never a failure: the infra checkout is optional.

Usage: check-realm-contract.py <workspace-dir>
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

REALM_FILES = [
    "orkyo-infra/compose/keycloak/orkyo-realm.json",
    "orkyo-community/release/config/keycloak/realm.json",
    "orkyo-community/infra/compose/keycloak/realm-local.json",
    "orkyo-saas/infra/compose/keycloak/realm-local.json",
    "orkyo-saas/local/realm-local.json",
]

# The contract: what the application reads from a realm by name.
REQUIRED_REALM_ROLES = {"user", "admin", "site-admin"}   # KeycloakClaims.RealmRolesClaim consumers
BACKEND_CLIENT_ID = "orkyo-backend"                    # KEYCLOAK_BACKEND_CLIENT_ID everywhere
LOGIN_THEME = "orkyo"                                  # foundation's Keycloak image

# The saas pair (orkyo.local stack vs the dev.sh stack) may differ ONLY here.
SAAS_BASE = "orkyo-saas/infra/compose/keycloak/realm-local.json"
SAAS_LOCAL = "orkyo-saas/local/realm-local.json"
URL_FIELDS = {"redirectUris", "webOrigins", "baseUrl", "rootUrl", "adminUrl"}
URL_ATTRIBUTES = {"post.logout.redirect.uris"}
# local/README.md: the fixed site-admin account exists in the orkyo.local stack only.
ALLOWED_EXTRA_LOCAL_USERS = {"admin@example.com"}

failures: list[str] = []


def fail(path: str, message: str) -> None:
    failures.append(f"{path}: {message}")
    print(f"::error file={path}::{message}")


def check_contract(path: str, realm: dict) -> None:
    roles = {r.get("name") for r in (realm.get("roles") or {}).get("realm", [])}
    missing = REQUIRED_REALM_ROLES - roles
    if missing:
        fail(path, f"realm roles missing: {sorted(missing)} (the application resolves these by name)")

    if realm.get("loginTheme") != LOGIN_THEME:
        fail(path, f"loginTheme is {realm.get('loginTheme')!r}, expected {LOGIN_THEME!r}")

    clients = {c.get("clientId"): c for c in realm.get("clients", [])}
    backend = clients.get(BACKEND_CLIENT_ID)
    if backend is None:
        fail(path, f"client {BACKEND_CLIENT_ID!r} missing")
    else:
        if backend.get("publicClient"):
            fail(path, f"client {BACKEND_CLIENT_ID!r} must be confidential (publicClient=false)")
        if not backend.get("serviceAccountsEnabled"):
            fail(path, f"client {BACKEND_CLIENT_ID!r} needs serviceAccountsEnabled (the API's admin calls)")

    # Same rule as orkyo-infra/scripts/ci/lint-realm-scopes.sh, applied to every copy: a listed
    # clientScopes key replaces Keycloak's built-ins (and loses the `basic` scope with `sub`).
    if "clientScopes" in realm:
        fail(path, "defines clientScopes; omit the key so Keycloak installs its built-ins")
    for client_id, client in clients.items():
        for key in ("defaultClientScopes", "optionalClientScopes"):
            if client.get(key):
                fail(path, f"client {client_id!r} lists {key}; omit it so the realm defaults apply")



def normalise_for_pair(realm: dict, drop_users: set[str]) -> dict:
    """The realm with the allowed differences blanked, for an equality check."""
    out = json.loads(json.dumps(realm))
    for client in out.get("clients", []):
        for key in URL_FIELDS:
            client.pop(key, None)
        attrs = client.get("attributes")
        if isinstance(attrs, dict):
            for key in URL_ATTRIBUTES:
                attrs.pop(key, None)
    out["users"] = [u for u in out.get("users", []) if u.get("username") not in drop_users]
    return out


def diff_paths(a, b, prefix="") -> list[str]:
    if isinstance(a, dict) and isinstance(b, dict):
        out = []
        for key in sorted(set(a) | set(b)):
            out += diff_paths(a.get(key), b.get(key), f"{prefix}.{key}")
        return out
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            return [f"{prefix}: {len(a)} vs {len(b)} entries"]
        out = []
        for i, (x, y) in enumerate(zip(a, b)):
            out += diff_paths(x, y, f"{prefix}[{i}]")
        return out
    return [] if a == b else [f"{prefix}: {a!r} vs {b!r}"]


def check_saas_pair(workspace: Path) -> None:
    base_path, local_path = workspace / SAAS_BASE, workspace / SAAS_LOCAL
    if not (base_path.is_file() and local_path.is_file()):
        return
    base = normalise_for_pair(json.loads(base_path.read_text()), set())
    local = normalise_for_pair(json.loads(local_path.read_text()), ALLOWED_EXTRA_LOCAL_USERS)
    diffs = diff_paths(base, local)
    if diffs:
        fail(SAAS_LOCAL, "differs from the dev.sh realm beyond the orkyo.local URLs and the admin fixture: "
             + "; ".join(diffs[:12]) + (" …" if len(diffs) > 12 else ""))


def main() -> int:
    workspace = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).resolve().parents[3])
    checked = 0
    for rel in REALM_FILES:
        path = workspace / rel
        if not path.is_file():
            print(f"::notice::{rel} not present in {workspace}, skipped")
            continue
        check_contract(rel, json.loads(path.read_text()))
        checked += 1
    check_saas_pair(workspace)
    if failures:
        print(f"check-realm-contract: {len(failures)} finding(s) across {checked} realm file(s)")
        return 1
    print(f"check-realm-contract: OK ({checked} realm file(s) keep the contract)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
