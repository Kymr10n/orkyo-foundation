#!/usr/bin/env bash
# keycloak-render-check.sh <image>
#
# Starts the given Keycloak image in dev mode with an in-memory database and the
# Orkyo theme as the realm default, follows the login redirect of the admin console
# client and requires the Orkyo login form to render with status 200. A FreeMarker
# template error in the theme answers 500 here, exactly as it does on a deployed
# realm, which no build step and no JSON probe can see.
set -euo pipefail

IMAGE="${1:?usage: keycloak-render-check.sh <image>}"
NAME="kc-render-check-$$"
PORT="${KC_RENDER_PORT:-18080}"
WORK="$(mktemp -d)"
trap 'docker rm -f "$NAME" >/dev/null 2>&1 || true; rm -rf "${WORK:?}"' EXIT

docker run -d --name "$NAME" -p "${PORT}:8080" \
  -e KC_DB=dev-mem -e KC_HTTP_ENABLED=true \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
  "$IMAGE" start-dev --db=dev-mem --spi-theme-default=orkyo >/dev/null

for _ in $(seq 1 100); do
  if curl -s -o /dev/null -w '%{http_code}' "http://localhost:${PORT}/realms/master" 2>/dev/null | grep -q '^200$'; then
    break
  fi
  sleep 3
done

LOGIN_URL="http://localhost:${PORT}/realms/master/protocol/openid-connect/auth?client_id=security-admin-console&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A${PORT}%2Fadmin%2Fmaster%2Fconsole%2F&scope=openid&state=render-check&nonce=render-check&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256"
STATUS=$(curl -s -L -c "$WORK/cookies" -b "$WORK/cookies" -o "$WORK/login.html" -w '%{http_code}' "$LOGIN_URL" || true)

if [[ "$STATUS" != "200" ]] || ! grep -q 'orkyo-login-form' "$WORK/login.html"; then
  echo "::error::Keycloak login page rendered with status ${STATUS} (expected 200 with the Orkyo form)"
  docker logs "$NAME" 2>&1 | grep -E 'FreeMarkerException|Caused by|in template' | head -20 || true
  exit 1
fi
if docker logs "$NAME" 2>&1 | grep -q 'FreeMarkerException'; then
  echo "::error::Keycloak logged a FreeMarker error while rendering the login page"
  docker logs "$NAME" 2>&1 | grep -E 'FreeMarkerException|Caused by|in template' | head -20
  exit 1
fi
echo "Keycloak login page renders with the Orkyo theme (status 200)"
