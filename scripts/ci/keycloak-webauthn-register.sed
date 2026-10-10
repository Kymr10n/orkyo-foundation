# The two lines the Orkyo theme changes in Keycloak's base login/resources/js/webauthnRegister.js.
# keycloak-render-check.sh applies this to the upstream file in the image and requires the
# result to equal keycloak/themes/orkyo/login/resources/js/webauthnRegister.js, so a Keycloak
# upgrade that changes the upstream script fails CI instead of drifting silently.
s|^import { base64url } from "rfc4648";$|import { base64url } from "rfc4648";\nimport { passkeyLabel } from "./passkeyLabel.js";|
s|^    let labelResult = window.prompt(initLabelPrompt, initLabel);$|    let labelResult = passkeyLabel(result, navigator.userAgent, initLabel);|
