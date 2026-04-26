<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=true displayInfo=false; section>
    <#if section = "form">
        <#-- Determine app URL: client.baseUrl > properties.orkyoAppUrl > omit -->
        <#assign appUrl = "">
        <#if (client.baseUrl)?has_content>
            <#assign appUrl = client.baseUrl>
        <#elseif properties.orkyoAppUrl?has_content>
            <#assign appUrl = properties.orkyoAppUrl>
        </#if>

        <div class="orkyo-info-content">
            <#if skipLink??>
            <#else>
                <div class="orkyo-form-actions">
                    <#if pageRedirectUri?has_content>
                        <a id="backToApp" href="${pageRedirectUri}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("backToApplication"))?no_esc}</a>
                        <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
                            // Auto-redirect after showing success message briefly
                            setTimeout(function() {
                                document.getElementById('backToApp').click();
                            }, 1500);
                        </script>
                    <#elseif actionUri?has_content>
                        <a href="${actionUri}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("proceedWithAction"))?no_esc}</a>
                    <#elseif appUrl?has_content>
                        <a id="backToApp" href="${appUrl}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("backToApplication"))?no_esc}</a>
                        <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
                            setTimeout(function() {
                                document.getElementById('backToApp').click();
                            }, 1500);
                        </script>
                    </#if>
                </div>
            </#if>
        </div>
    </#if>
</@layout.registrationLayout>
