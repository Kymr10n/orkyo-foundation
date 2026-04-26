<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('firstName','lastName','email','username','password','password-confirm'); section>
    <#if section = "form">
        <h2 class="orkyo-form-heading">Create Account</h2>
        <form id="kc-register-form" action="${url.registrationAction}" method="post">
            <#if !realm.registrationEmailAsUsername>
                <div class="orkyo-form-group">
                    <label for="username" class="orkyo-label">${msg("username")}</label>
                    <input type="text" id="username" class="orkyo-input" name="username" 
                           value="${(register.formData.username!'')}" 
                           autocomplete="username"
                           aria-invalid="<#if messagesPerField.existsError('username')>true</#if>" />
                    <#if messagesPerField.existsError('username')>
                        <span class="orkyo-error">${kcSanitize(messagesPerField.get('username'))?no_esc}</span>
                    </#if>
                </div>
            </#if>

            <div class="orkyo-form-group">
                <label for="firstName" class="orkyo-label">${msg("firstName")}</label>
                <input type="text" id="firstName" class="orkyo-input" name="firstName" 
                       value="${(register.formData.firstName!'')}" 
                       autocomplete="given-name"
                       aria-invalid="<#if messagesPerField.existsError('firstName')>true</#if>" />
                <#if messagesPerField.existsError('firstName')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.get('firstName'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <label for="lastName" class="orkyo-label">${msg("lastName")}</label>
                <input type="text" id="lastName" class="orkyo-input" name="lastName" 
                       value="${(register.formData.lastName!'')}" 
                       autocomplete="family-name"
                       aria-invalid="<#if messagesPerField.existsError('lastName')>true</#if>" />
                <#if messagesPerField.existsError('lastName')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.get('lastName'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <label for="email" class="orkyo-label">${msg("email")}</label>
                <input type="email" id="email" class="orkyo-input" name="email" 
                       value="${(register.formData.email!'')}" 
                       autocomplete="email"
                       aria-invalid="<#if messagesPerField.existsError('email')>true</#if>" />
                <#if messagesPerField.existsError('email')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.get('email'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <label for="password" class="orkyo-label">${msg("password")}</label>
                <input type="password" id="password" class="orkyo-input" name="password" 
                       autocomplete="new-password"
                       aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>" />
                <#if messagesPerField.existsError('password')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.get('password'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <label for="password-confirm" class="orkyo-label">${msg("passwordConfirm")}</label>
                <input type="password" id="password-confirm" class="orkyo-input" name="password-confirm"
                       autocomplete="new-password"
                       aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>" />
                <#if messagesPerField.existsError('password-confirm')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}</span>
                </#if>
            </div>

            <#if recaptchaRequired??>
                <div class="orkyo-form-group">
                    <div class="g-recaptcha" data-size="compact" data-sitekey="${recaptchaSiteKey}"></div>
                </div>
            </#if>

            <div class="orkyo-form-actions">
                <button class="orkyo-button-primary" type="submit">
                    ${msg("doRegister")}
                </button>
            </div>

            <div class="orkyo-form-footer">
                <a href="${url.loginUrl}" class="orkyo-link">${msg("backToLogin")}</a>
            </div>
        </form>
    </#if>
</@layout.registrationLayout>
