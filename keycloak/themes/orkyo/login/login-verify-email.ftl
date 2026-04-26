<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=true displayInfo=false; section>
    <#if section = "form">
        <div class="orkyo-verify-email">
            <h2 class="orkyo-form-heading">Verify Your Email</h2>
            <p class="orkyo-info-text">
                ${msg("emailVerifyInstruction1",user.email)}
            </p>
            <p class="orkyo-info-text orkyo-text-muted">
                ${msg("emailVerifyInstruction2")}
                <br/>
                <a href="${url.loginAction}" class="orkyo-link">${msg("doClickHere")}</a>
                ${msg("emailVerifyInstruction3")}
            </p>
        </div>
    </#if>
</@layout.registrationLayout>
