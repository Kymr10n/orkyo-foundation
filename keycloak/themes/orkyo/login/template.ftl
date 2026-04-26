<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html style="background:#0c0d0f">
<head>
    <meta charset="utf-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
    <meta name="robots" content="noindex, nofollow">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    
    <#if properties.meta?has_content>
        <#list properties.meta?split(' ') as meta>
            <meta name="${meta?split('==')[0]}" content="${meta?split('==')[1]}"/>
        </#list>
    </#if>
    
    <title>${msg("loginTitle",(realm.displayName!''))}</title>
    <link rel="icon" href="${url.resourcesPath}/img/orkyo-logo.png" />
    
    <#-- Theme detection BEFORE stylesheet links — scripts wait for preceding
         stylesheets, so placing this first avoids a white flash while CSS loads -->
    <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
      (function() {
        var m = document.cookie.match(/(?:^|;\s*)orkyo-theme=([^;]*)/);
        var theme = m ? m[1] : null;
        var isLight = theme === 'light' ||
          ((!theme || theme === 'system') && window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches);
        if (isLight) document.documentElement.classList.add('light');
        document.documentElement.style.background = isLight ? '#ffffff' : '#0c0d0f';
      })();
    </script>

    <#if properties.stylesCommon?has_content>
        <#list properties.stylesCommon?split(' ') as style>
            <link href="${url.resourcesCommonPath}/${style}" rel="stylesheet" />
        </#list>
    </#if>
    <#if properties.styles?has_content>
        <#list properties.styles?split(' ') as style>
            <link href="${url.resourcesPath}/${style}" rel="stylesheet" />
        </#list>
    </#if>
    <style>body{visibility:hidden}</style>
    <noscript><style>body{visibility:visible}</style></noscript>
</head>

<body class="orkyo-login">
    <div class="orkyo-login-container">
        <div class="orkyo-login-card">
            <#-- Logo and branding -->
            <div class="orkyo-login-header">
                <img src="${url.resourcesPath}/img/orkyo-logo.png" alt="Orkyo" class="orkyo-logo" />
                <h1 class="orkyo-title">Orkyo</h1>
                <p class="orkyo-subtitle">Production Space Planning</p>
            </div>
            
            <#-- Alert messages -->
            <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
                <div class="orkyo-alert orkyo-alert-${message.type}" role="alert">
                    ${kcSanitize(message.summary)?no_esc}
                </div>
            </#if>

            <#-- Main form content -->
            <div class="orkyo-login-form">
                <#nested "form">
            </div>

            <#-- Info section (like registration link - hidden for invite-only) -->
            <#if displayInfo>
                <div class="orkyo-login-info">
                    <#nested "info">
                </div>
            </#if>
        </div>
    </div>
<script<#if cspNonce??> nonce="${cspNonce}"</#if>>document.body.style.visibility='visible'</script>
</body>
</html>
</#macro>
