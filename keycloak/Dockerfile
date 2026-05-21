FROM quay.io/keycloak/keycloak:26.6.2@sha256:5afd40414096432903a5374ba4ba70b931dcca598a6bd9f44239fe66ef8d9737

# Bake the complete Orkyo theme into the image.
# All theme files (templates, CSS, messages, theme.properties) live here in foundation.
# Infra does FROM this image with zero mutation.
COPY themes/orkyo /opt/keycloak/themes/orkyo

# Pre-build the server so startup uses --optimized (no rebuild on container start)
ENV KC_HEALTH_ENABLED=true \
    KC_FEATURES=recovery-codes \
    KC_DB=postgres
RUN /opt/keycloak/bin/kc.sh build
