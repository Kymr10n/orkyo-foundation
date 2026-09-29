#!/usr/bin/env bash
# verify-nonroot.sh — fail when a container image is configured to run as root.
#
# Reads the configured user from the image metadata: it needs no `id` binary in
# the image and ignores the entrypoint. A pull or inspect failure fails the
# script; there is no fallback UID.
#
# Usage: scripts/ci/verify-nonroot.sh <image>
set -euo pipefail

image="${1:?Usage: $0 <image>}"
docker pull --quiet "$image"
user=$(docker image inspect --format '{{.Config.User}}' "$image")
echo "Container user: '${user:-<unset>}'"
case "${user%%:*}" in
  ""|0|root)
    echo "::error::$image runs as root — must use non-root user"
    exit 1
    ;;
esac
