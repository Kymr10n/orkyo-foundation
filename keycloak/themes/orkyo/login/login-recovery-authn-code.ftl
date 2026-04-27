<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('recoveryCodeInput'); section>
    <#if section = "form">
        <div class="orkyo-recovery-code">
            <div class="orkyo-info-title">${msg("recoveryCodesTitle")}</div>
            <p class="orkyo-info-text orkyo-text-muted">${msg("recoveryCodeEnter", recoveryAuthnCodesInputBean.codeNumber?c)}</p>

            <form id="kc-recovery-code-login-form" action="${url.loginAction}" method="post">
                <div class="orkyo-form-group">
                    <label for="recoveryCodeInput" class="orkyo-label">
                        ${msg("recoveryCodeInputLabel", recoveryAuthnCodesInputBean.codeNumber?c)}
                    </label>
                    <input
                        tabindex="1"
                        id="recoveryCodeInput"
                        name="recoveryCodeInput"
                        class="orkyo-input orkyo-input-mono"
                        type="text"
                        autocomplete="off"
                        spellcheck="false"
                        autofocus
                        aria-invalid="<#if messagesPerField.existsError('recoveryCodeInput')>true</#if>"
                    />
                    <#if messagesPerField.existsError('recoveryCodeInput')>
                        <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('recoveryCodeInput'))?no_esc}</span>
                    </#if>
                </div>

                <div class="orkyo-recovery-notice">
                    <span class="orkyo-text-muted orkyo-text-sm">${msg("recoveryCodeWarning")}</span>
                </div>

                <div class="orkyo-form-actions">
                    <button tabindex="2" class="orkyo-button-primary" id="kc-login" name="login" type="submit">
                        ${msg("doLogIn")}
                    </button>
                </div>

            </form>

                <#-- Try another way (go back to OTP) -->
                <#if auth.showTryAnotherWayLink()>
                    <div class="orkyo-form-footer">
                        <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post">
                            <input type="hidden" name="tryAnotherWay" value="on"/>
                            <a href="#" id="try-another-way"
                               onclick="document.forms['kc-select-try-another-way-form'].submit(); return false;"
                               class="orkyo-link">
                                ${msg("doTryAnotherWay")}
                            </a>
                        </form>
                    </div>
                </#if>
        </div>
    </#if>
</@layout.registrationLayout>
