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

        <#-- Keycloak sets skipLink when the flow has no client to return to (an account
             update finished outside a client redirect, for example). It sets a Boolean, so
             read the value: an existence test (skipLink??) also matches skipLink=false and
             would hide the link in flows that never asked for it to be hidden. -->
        <#assign noClientToReturnTo = (skipLink!false)>

        <div class="orkyo-info-content">
            <div class="orkyo-form-actions">
                <#if !noClientToReturnTo && pageRedirectUri?has_content>
                    <a id="backToApp" href="${pageRedirectUri}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("backToApplication"))?no_esc}</a>
                    <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
                        // Auto-redirect after showing success message briefly
                        setTimeout(function() {
                            document.getElementById('backToApp').click();
                        }, 1500);
                    </script>
                <#elseif !noClientToReturnTo && actionUri?has_content>
                    <a href="${actionUri}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("proceedWithAction"))?no_esc}</a>
                <#elseif appUrl?has_content>
                    <#-- Last resort, offered even when Keycloak suppressed the client link:
                         the product's own front door is always somewhere to go, and a success
                         page with no way forward strands the person. The auto-redirect is not
                         offered here — without a client context, moving them is presumptuous. -->
                    <a id="backToApp" href="${appUrl}" class="orkyo-button-primary orkyo-button-link">${kcSanitize(msg("backToApplication"))?no_esc}</a>
                    <#if !noClientToReturnTo>
                        <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
                            setTimeout(function() {
                                document.getElementById('backToApp').click();
                            }, 1500);
                        </script>
                    </#if>
                </#if>
            </div>
        </div>
    </#if>
</@layout.registrationLayout>
