using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Helpers;

/// <summary>
/// A bare <see cref="HttpContext"/> for executing an <see cref="IResult"/> without a host, and a
/// reader for the JSON it wrote.
/// </summary>
internal static class HttpContextTestHelpers
{
    /// <summary>A context with logging services and a readable response body.</summary>
    public static DefaultHttpContext CreateHttpContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    /// <summary>The JSON the response body holds, read from the start.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return json.RootElement.Clone();
    }
}
