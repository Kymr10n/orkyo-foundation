<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('password','password-confirm') displayInfo=false; section>
    <#if section = "form">
        <form id="kc-passwd-update-form" action="${url.loginAction}" method="post">
            <input type="text" id="username" name="username" value="${username}" autocomplete="username" readonly="readonly" style="display:none;"/>
            <input type="password" id="password" name="password" autocomplete="current-password" style="display:none;"/>

            <div class="orkyo-form-group">
                <label for="password-new" class="orkyo-label">${msg("passwordNew")}</label>
                <input type="password" id="password-new" name="password-new" class="orkyo-input" autofocus autocomplete="new-password" aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>" />
                <#if messagesPerField.existsError('password')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('password'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <label for="password-confirm" class="orkyo-label">${msg("passwordConfirm")}</label>
                <input type="password" id="password-confirm" name="password-confirm" class="orkyo-input" autocomplete="new-password" aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>" />
                <#if messagesPerField.existsError('password-confirm')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('password-confirm'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-actions">
                <#if isAppInitiatedAction??>
                    <button class="orkyo-button-primary" type="submit">${msg("doSubmit")}</button>
                    <button class="orkyo-button-secondary" type="submit" name="cancel-aia" value="true">${msg("doCancel")}</button>
                <#else>
                    <button class="orkyo-button-primary" type="submit">${msg("doSubmit")}</button>
                </#if>
            </div>
        </form>
    </#if>
</@layout.registrationLayout>
