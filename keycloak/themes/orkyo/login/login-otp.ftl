<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp') displayInfo=false; section>
    <#if section = "form">
        <h2 class="orkyo-form-heading">Two-Factor Authentication</h2>
        <p class="orkyo-info-text" style="text-align:center; margin-bottom: 1.25rem;">
            Enter the 6-digit code from your authenticator app.
        </p>
        <form action="${url.loginAction}" method="post">
            <div class="orkyo-form-group">
                <label for="otp" class="orkyo-label">${msg("loginOtpOneTime")}</label>
                <input type="text" id="otp" name="otp" class="orkyo-input"
                       autocomplete="one-time-code" inputmode="numeric" pattern="[0-9]*"
                       maxlength="8"
                       autofocus
                       aria-invalid="<#if messagesPerField.existsError('totp')>true</#if>" />
                <#if messagesPerField.existsError('totp')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('totp'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-actions">
                <button type="submit" class="orkyo-button-primary">${msg("doLogIn")}</button>
            </div>
        </form>
    </#if>
</@layout.registrationLayout>
