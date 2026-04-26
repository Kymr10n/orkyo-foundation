<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=messagesPerField.exists('global') displayRequiredFields=true; section>
    <#if section = "form">
        <h2 class="orkyo-form-heading">${msg("loginProfileTitle")}</h2>
        <form id="kc-update-profile-form" action="${url.loginAction}" method="post">

            <#list profile.attributes as attribute>
                <div class="orkyo-form-group">
                    <label for="${attribute.name}" class="orkyo-label">
                        ${advancedMsg(attribute.displayName!'')}
                        <#if attribute.required> *</#if>
                    </label>

                    <#if attribute.annotations.inputType?? && attribute.annotations.inputType == 'textarea'>
                        <textarea id="${attribute.name}" name="${attribute.name}" class="orkyo-input"
                            aria-invalid="<#if messagesPerField.existsError('${attribute.name}')>true</#if>"
                            <#if attribute.readOnly>disabled</#if>
                        >${(attribute.value!'')}</textarea>
                    <#elseif attribute.annotations.inputType?? && (attribute.annotations.inputType == 'select' || attribute.annotations.inputType == 'multiselect')>
                        <select id="${attribute.name}" name="${attribute.name}" class="orkyo-input"
                            aria-invalid="<#if messagesPerField.existsError('${attribute.name}')>true</#if>"
                            <#if attribute.readOnly>disabled</#if>
                            <#if attribute.annotations.inputType == 'multiselect'>multiple</#if>
                        >
                            <#if attribute.annotations.inputType == 'select'>
                                <option value=""></option>
                            </#if>
                            <#if attribute.validators.options?? && attribute.validators.options.options??>
                                <#list attribute.validators.options.options as option>
                                    <option value="${option}" <#if attribute.values?seq_contains(option)>selected</#if>>${option}</option>
                                </#list>
                            </#if>
                        </select>
                    <#else>
                        <input type="<#if attribute.annotations.inputType??><#if attribute.annotations.inputType?starts_with('html5-')>${attribute.annotations.inputType[6..]}<#else>${attribute.annotations.inputType}</#if><#else>text</#if>"
                            id="${attribute.name}" name="${attribute.name}"
                            value="${(attribute.value!'')}"
                            class="orkyo-input"
                            aria-invalid="<#if messagesPerField.existsError('${attribute.name}')>true</#if>"
                            <#if attribute.readOnly>disabled</#if>
                            <#if attribute.autocomplete??>autocomplete="${attribute.autocomplete}"</#if>
                            <#if attribute.annotations.inputTypePlaceholder??>placeholder="${advancedMsg(attribute.annotations.inputTypePlaceholder)}"</#if>
                        />
                    </#if>

                    <#if messagesPerField.existsError('${attribute.name}')>
                        <span class="orkyo-error">${kcSanitize(messagesPerField.get('${attribute.name}'))?no_esc}</span>
                    </#if>
                </div>
            </#list>

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
