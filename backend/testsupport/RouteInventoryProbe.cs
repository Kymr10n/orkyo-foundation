using System.Net;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// Proves a foundation endpoint group is mapped in a product's <c>Program.cs</c>: an
/// unauthenticated GET returns 401 when the route is registered (auth intercepts) and 404 when
/// it is not, so "anything but 404" means the <c>Map*Endpoints()</c> call is present. A 500 also
/// fails: the route is mapped but a service or rate-limit policy behind it is not registered.
/// </summary>
/// <remarks>
/// Failure mode this catches: a foundation refactor deletes or renames an endpoint map and the
/// corresponding line in the product's <c>Program.cs</c> is silently missed — the API still boots
/// (no DI dependency), but every route under the removed map 404s. Each product supplies its own
/// probe paths as <c>[InlineData]</c>; the paths are chosen to be unique to one map call so a
/// single missing map produces exactly one failing case.
/// </remarks>
public static class RouteInventoryProbe
{
    /// <summary>
    /// Sends an unauthenticated GET to <paramref name="path"/>. Returns null when the route is
    /// served, or a diagnostic naming <paramref name="expectedMapCall"/> when it is not.
    /// </summary>
    public static async Task<string?> ProbeAsync(HttpClient client, string path, string expectedMapCall)
    {
        ArgumentNullException.ThrowIfNull(client);
        var response = await client.GetAsync(path);

        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.InternalServerError))
            return null;

        return $"Route '{path}' returned {(int)response.StatusCode}. 404 = likely missing '{expectedMapCall}()' "
             + "call in Program.cs; 500 = the route is mapped but a service or rate-limit policy behind it "
             + "is not registered.";
    }
}
