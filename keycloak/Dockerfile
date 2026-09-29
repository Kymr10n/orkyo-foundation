FROM quay.io/keycloak/keycloak:26.7.4@sha256:82a77884f3af238beab1e7afd63b5f530e1b5c0590bd7aa60b40a40463e29b2c

# Bake the complete Orkyo theme into the image.
# All theme files (templates, CSS, messages, theme.properties) live here in foundation.
# Infra does FROM this image with zero mutation.
COPY themes/orkyo /opt/keycloak/themes/orkyo

# Pre-build the server so startup uses --optimized (no rebuild on container start)
ENV KC_HEALTH_ENABLED=true \
    KC_FEATURES=recovery-codes \
    KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build
