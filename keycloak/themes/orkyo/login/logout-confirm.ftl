<#import "template.ftl" as layout>
<@layout.registrationLayout; section>
    <#if section = "header">
        Sign out of Orkyo
    <#elseif section = "form">
        <div class="orkyo-form">
            <p class="orkyo-info-text" style="margin-bottom: 24px;">
                Are you sure you want to sign out?
            </p>
            <form action="${url.logoutConfirmAction}" method="POST">
                <input type="hidden" name="session_code" value="${logoutConfirm.code}">
                <button type="submit" class="orkyo-button orkyo-button-primary" style="width: 100%;">
                    Sign Out
                </button>
            </form>
            <div style="margin-top: 16px; text-align: center;">
                <a href="${url.loginUrl}" class="orkyo-link">Cancel</a>
            </div>
        </div>
    </#if>
</@layout.registrationLayout>
