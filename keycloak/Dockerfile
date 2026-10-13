FROM quay.io/keycloak/keycloak:26.8.0@sha256:b0f60d489d51c5d113390bdf5461d4c06e6051be026c05549f2e1e10ec352bcc
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
