using System.Net;
using Microsoft.AspNetCore.Http;

namespace Api.Endpoints;

/// <summary>
/// The self-contained, Orkyo-branded page behind the public announcement unsubscribe link: the
/// confirmation form the link opens, and the result the form's POST answers with. The recipient
/// may be logged out, so the page does not redirect into the SPA.
/// </summary>
internal static class UnsubscribePage
{
    // Inline styles only; the endpoint relaxes the API's strict CSP for them.
    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{HEADING}} — Orkyo</title>
          <style>
            :root { color-scheme: light; }
            * { box-sizing: border-box; }
            body {
              font-family: system-ui, -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
              margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
              background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 24px; color: #18181b;
            }
            .card {
              background: #fff; width: 100%; max-width: 460px; border-radius: 16px; overflow: hidden;
              box-shadow: 0 20px 50px rgba(0,0,0,0.25); text-align: center;
            }
            .brand {
              background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
              color: #fff; padding: 22px; font-size: 22px; font-weight: 800; letter-spacing: .5px;
            }
            .body { padding: 36px 40px 40px; }
            .icon {
              width: 64px; height: 64px; line-height: 64px; margin: 0 auto 22px; border-radius: 50%;
              background: {{ICON_BG}}; color: #fff; font-size: 30px; font-weight: 700;
            }
            h1 { margin: 0 0 12px; font-size: 22px; }
            p { margin: 0 0 28px; font-size: 15px; line-height: 1.6; color: #52525b; }
            .btn {
              display: inline-block; padding: 12px 26px; border-radius: 9px; text-decoration: none; border: 0;
              font: inherit; font-size: 14px; font-weight: 600; color: #fff; cursor: pointer;
              background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="brand">Orkyo</div>
            <div class="body">
              <div class="icon">{{ICON}}</div>
              <h1>{{HEADING}}</h1>
              <p>{{MESSAGE}}</p>
              {{ACTION}}
            </div>
          </div>
        </body>
        </html>
        """;

    private const string Brand = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";

    /// <summary>The page the email link opens: nothing changes until the recipient presses the button.</summary>
    public static IResult Confirm(Guid token) => Render(
        "&#63;", Brand, "Unsubscribe from announcements?",
        "You will stop receiving announcement emails. You will still see announcements in the app.",
        $"""<form method="post"><input type="hidden" name="token" value="{token}"><button class="btn" type="submit">Unsubscribe</button></form>""");

    /// <summary>The outcome, with a link back to the app.</summary>
    public static IResult Result(string appBaseUrl, bool success, string heading, string message) => Render(
        success ? "&#10003;" : "&#33;", success ? Brand : "#dc2626", heading, message,
        $"""<a class="btn" href="{WebUtility.HtmlEncode(appBaseUrl)}">Back to Orkyo</a>""");

    private static IResult Render(string icon, string iconBackground, string heading, string message, string action)
    {
        var html = Template
            .Replace("{{ICON_BG}}", iconBackground)
            .Replace("{{ICON}}", icon)
            .Replace("{{HEADING}}", WebUtility.HtmlEncode(heading))
            .Replace("{{MESSAGE}}", WebUtility.HtmlEncode(message))
            .Replace("{{ACTION}}", action);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
