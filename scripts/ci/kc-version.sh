#!/usr/bin/env bash
# kc-version.sh — print KC_VERSION=<major>.<minor> for the Keycloak image tag.
#
# The prefix of every published Keycloak tag (<major>.<minor>-orkyo-<version>) comes
# from the upstream base image pinned in keycloak/Dockerfile, so a Dependabot bump of
# that pin moves the tag prefix with it. release-ci.yml appends the output to
# $GITHUB_ENV in each job that builds, scans, promotes or tags the image.
#
# Usage: scripts/ci/kc-version.sh [<dockerfile>]   (default: keycloak/Dockerfile)
set -euo pipefail

dockerfile="${1:-keycloak/Dockerfile}"
version=$(sed -nE 's#^FROM[[:space:]]+quay\.io/keycloak/keycloak:([0-9]+\.[0-9]+)\.[0-9]+([@[:space:]].*)?$#\1#p' "$dockerfile" | head -n1)
if [ -z "$version" ]; then
  echo "::error::No 'FROM quay.io/keycloak/keycloak:<major>.<minor>.<patch>' line in $dockerfile" >&2
  exit 1
fi
echo "KC_VERSION=$version"
