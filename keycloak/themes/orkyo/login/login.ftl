<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username','password') displayInfo=true; section>
    <#if section = "form">
        <h2 class="orkyo-form-heading">${msg("doLogIn")}</h2>
        <form id="kc-form-login" onsubmit="handleLoginSubmit(this); return true;" action="${url.loginAction}" method="post">
            <div class="orkyo-form-group">
                <label for="username" class="orkyo-label">${msg("usernameOrEmail")}</label>
                <input tabindex="1" id="username" class="orkyo-input" name="username" value="${(login.username!'')}" type="text" autofocus autocomplete="username" aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>" />
                <#if messagesPerField.existsError('username','password')>
                    <span class="orkyo-error">${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}</span>
                </#if>
            </div>

            <div class="orkyo-form-group">
                <div class="orkyo-label-row">
                    <label for="password" class="orkyo-label">${msg("password")}</label>
                    <#if realm.resetPasswordAllowed>
                        <a tabindex="5" href="${url.loginResetCredentialsUrl}" class="orkyo-link orkyo-label-link">${msg("doForgotPassword")}</a>
                    </#if>
                </div>
                <div class="orkyo-password-wrapper">
                    <input tabindex="2" id="password" class="orkyo-input" name="password" type="password" autocomplete="current-password" aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>" />
                    <button type="button" class="orkyo-password-toggle" aria-label="Show password" onclick="togglePassword()">
                        <svg id="icon-eye" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg>
                        <svg id="icon-eye-off" xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="display:none"><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/><line x1="2" x2="22" y1="2" y2="22"/></svg>
                    </button>
                </div>
            </div>

            <div class="orkyo-form-options">
                <#if realm.rememberMe && !usernameHidden??>
                    <label class="orkyo-checkbox">
                        <input tabindex="3" id="rememberMe" name="rememberMe" type="checkbox" <#if login.rememberMe??>checked</#if>>
                        <span>${msg("rememberMe")}</span>
                    </label>
                </#if>
            </div>

            <div class="orkyo-form-actions">
                <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>/>
                <button tabindex="4" class="orkyo-button-primary" name="login" id="kc-login" type="submit">
                    <span id="login-label">${msg("doLogIn")}</span>
                    <span id="login-spinner" style="display:none">Signing in…</span>
                </button>
            </div>
        </form>

        <#if realm.password && social?? && social.providers?has_content>
            <div class="orkyo-social-divider">
                <span>or</span>
            </div>
            <div class="orkyo-social-providers">
                <#list social.providers as p>
                    <a id="social-${p.alias}" class="orkyo-social-button orkyo-social-${p.alias}" href="${p.loginUrl}">
                        <#if p.alias == "google">
                            <svg class="orkyo-social-icon" width="18" height="18" viewBox="0 0 18 18" xmlns="http://www.w3.org/2000/svg"><path d="M17.64 9.2c0-.637-.057-1.251-.164-1.84H9v3.481h4.844a4.14 4.14 0 0 1-1.796 2.716v2.259h2.908c1.702-1.567 2.684-3.875 2.684-6.615Z" fill="#4285F4"/><path d="M9 18c2.43 0 4.467-.806 5.956-2.18l-2.908-2.259c-.806.54-1.837.86-3.048.86-2.344 0-4.328-1.584-5.036-3.711H.957v2.332A8.997 8.997 0 0 0 9 18Z" fill="#34A853"/><path d="M3.964 10.71A5.41 5.41 0 0 1 3.682 9c0-.593.102-1.17.282-1.71V4.958H.957A8.997 8.997 0 0 0 0 9c0 1.452.348 2.827.957 4.042l3.007-2.332Z" fill="#FBBC05"/><path d="M9 3.58c1.321 0 2.508.454 3.44 1.345l2.582-2.58C13.463.891 11.426 0 9 0A8.997 8.997 0 0 0 .957 4.958L3.964 6.29C4.672 4.163 6.656 2.58 9 3.58Z" fill="#EA4335"/></svg>
                        </#if>
                        <span>Continue with ${p.displayName!}</span>
                    </a>
                </#list>
            </div>
        </#if>

        <script<#if cspNonce??> nonce="${cspNonce}"</#if>>
          function togglePassword() {
            var input = document.getElementById('password');
            var eyeOn = document.getElementById('icon-eye');
            var eyeOff = document.getElementById('icon-eye-off');
            var btn = input.nextElementSibling;
            if (input.type === 'password') {
              input.type = 'text';
              eyeOn.style.display = 'none';
              eyeOff.style.display = '';
              btn.setAttribute('aria-label', 'Hide password');
            } else {
              input.type = 'password';
              eyeOn.style.display = '';
              eyeOff.style.display = 'none';
              btn.setAttribute('aria-label', 'Show password');
            }
          }

          function handleLoginSubmit(form) {
            var btn = document.getElementById('kc-login');
            document.getElementById('login-label').style.display = 'none';
            document.getElementById('login-spinner').style.display = '';
            btn.disabled = true;
          }
        </script>
    </#if>

    <#if section = "info">
        <#if realm.registrationAllowed>
            <div class="orkyo-request-access">
                <span>Don't have an account?</span>
                <a href="${url.registrationUrl}" class="orkyo-link">Create account</a>
            </div>
        </#if>
    </#if>
</@layout.registrationLayout>
