<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username') displayInfo=false; section>
    <#if section = "form">
        <div class="orkyo-reset-password">
            <h2 class="orkyo-form-heading">Reset Your Password</h2>
            <p class="orkyo-info-text">
                Enter your email address and we'll send you a link to reset your password.
            </p>
            <form id="kc-reset-password-form" action="${url.loginAction}" method="post">
                <div class="orkyo-form-group">
                    <label for="username" class="orkyo-label">${msg("usernameOrEmail")}</label>
                    <input type="text" id="username" name="username" class="orkyo-input" autofocus value="${(auth.attemptedUsername!'')}" aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"/>
                    <#if messagesPerField.existsError('username')>
                        <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('username'))?no_esc}</span>
                    </#if>
                </div>

                <div class="orkyo-form-actions">
                    <button class="orkyo-button-primary" type="submit">${msg("doSubmit")}</button>
                </div>

                <div class="orkyo-form-footer">
                    <a href="${url.loginUrl}" class="orkyo-link">${msg("backToLogin")}</a>
                </div>
            </form>
        </div>
    </#if>
</@layout.registrationLayout>
