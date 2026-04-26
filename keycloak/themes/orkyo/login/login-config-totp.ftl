<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp') displayInfo=false; section>
    <#if section = "form">
        <div class="orkyo-totp-setup">
            <h2 class="orkyo-form-heading">Set Up Two-Factor Authentication</h2>
            <p class="orkyo-info-text">
                Scan the QR code with your authenticator app (e.g. Google Authenticator, Microsoft Authenticator, or Authy), then enter the 6-digit code to verify setup.
            </p>

            <div class="orkyo-totp-qr">
                <img src="data:image/png;base64, ${totp.totpSecretQrCode}"
                     alt="QR code — scan with your authenticator app, or use the manual key below" />
            </div>

            <details class="orkyo-totp-manual">
                <summary class="orkyo-link">Can't scan?</summary>
                <div class="orkyo-totp-secret-row" style="margin-top: 0.5rem;">
                    <span class="orkyo-totp-secret-code" id="totp-secret-display">${totp.totpSecretEncoded?replace("(.{4})", "$1 ", "r")?trim}</span>
                    <button type="button" class="orkyo-copy-btn" onclick="copyTotpSecret()">Copy</button>
                </div>
            </details>

            <form action="${url.loginAction}" method="post">
                <input type="hidden" id="totpSecret" name="totpSecret" value="${totp.totpSecret}" />
                <input type="hidden" name="userLabel" value="Authenticator" />

                <div class="orkyo-form-group">
                    <label for="totp" class="orkyo-label">${msg("authenticatorCode")}</label>
                    <input type="text" id="totp" name="totp" class="orkyo-input"
                           autocomplete="one-time-code" inputmode="numeric" pattern="[0-9]*"
                           maxlength="8"
                           autofocus
                           aria-invalid="<#if messagesPerField.existsError('totp')>true</#if>" />
                    <#if messagesPerField.existsError('totp')>
                        <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('totp'))?no_esc}</span>
                    </#if>
                </div>

                <div class="orkyo-form-actions">
                    <button type="submit" class="orkyo-button-primary">${msg("doSubmit")}</button>
                </div>
            </form>
        </div>

        <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
          function copyTotpSecret() {
            var raw = '${totp.totpSecretEncoded}';
            navigator.clipboard.writeText(raw).then(function() {
              var btn = document.querySelector('.orkyo-copy-btn');
              btn.textContent = 'Copied!';
              setTimeout(function() { btn.textContent = 'Copy'; }, 2000);
            });
          }
        </script>
    </#if>
</@layout.registrationLayout>
