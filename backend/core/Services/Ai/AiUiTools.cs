using System.Text.Json;
using Api.Models;

namespace Api.Services.Ai;

/// <summary>
/// The tool that moves the person's screen.
///
/// Definition-only, like <see cref="AiProposalTools"/>: there is nothing to run on the
/// server, because navigation happens in the browser. The loop turns a call into a
/// <c>UiAction</c> event and answers the model with a tool result, then carries on — so the
/// assistant can open a page and keep talking in the same turn.
///
/// This does not weaken the read-only promise. It changes what is on screen, never what is
/// in the workspace, and the person can navigate away from anything it opens.
/// </summary>
public static class AiUiTools
{
    public const string OpenView = "open_view";

    public static bool IsUiAction(string toolName) => toolName is OpenView;

    /// <summary>
    /// The tool as this person may use it: the view enum carries only what their role can
    /// reach, so the model is never offered a door it would be refused at.
    /// </summary>
    public static AiToolDefinition DefinitionFor(bool canEdit, bool isAdmin)
    {
        var views = AiViewCatalog.For(canEdit, isAdmin);

        var enumJson = string.Join(", ", views.Select(v => JsonSerializer.Serialize(v.Id)));
        var catalogue = string.Join("\n", views.Select(v => $"- {v.Id}: {v.Description}"));

        return new AiToolDefinition
        {
            Name = OpenView,
            Description =
                "Take the person to a place in the app — a page, or one record opened for editing. " +
                "Use it when showing beats describing: they asked where something is, or you have just " +
                "named a specific record they will want to look at. It changes nothing in the workspace, " +
                "only what is on their screen, so it needs no confirmation. Say what you opened, and keep " +
                "answering afterwards — opening a page does not end your turn. Do not open the same view " +
                "twice in one turn, and do not open anything if they only asked a question you have " +
                "already answered in words.\n\nViews:\n" + catalogue,
            InputSchemaJson = $$"""
            {
              "type": "object",
              "properties": {
                "view": { "type": "string", "enum": [{{enumJson}}], "description": "Which view to open." },
                "entityId": { "type": "string", "description": "The record to open. Required for the single-record views (request, resource, site, template, criterion)." },
                "siteId": { "type": "string", "description": "Optional. Switch to this site first, when the record belongs to a site other than the one on screen." }
              },
              "required": ["view"]
            }
            """,
        };
    }
}
