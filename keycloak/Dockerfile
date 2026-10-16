FROM quay.io/keycloak/keycloak:26.8.1@sha256:79c623a22abacf3fe6c00a5fb30f5b7843d717b234033b80995891c06116d244
# The image tag prefix (<major>.<minor>-orkyo-) is derived from this FROM line by
# scripts/ci/kc-version.sh. A change here starts a new tag line, and the first release
# on that line must build an image from a commit that touches keycloak/**. The same
# holds for a new Orkyo major (the -orkyo-<major> tag): 1.0.0 is the first release on
# the -orkyo-1 line, so this commit exists to build its image.

# Bake the complete Orkyo theme into the image.
# All theme files (templates, CSS, messages, theme.properties) live here in foundation.
# Infra does FROM this image with zero mutation.
COPY themes/orkyo /opt/keycloak/themes/orkyo

# Pre-build the server so startup uses --optimized (no rebuild on container start)
ENV KC_HEALTH_ENABLED=true \
    KC_FEATURES=recovery-codes \
    KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build
