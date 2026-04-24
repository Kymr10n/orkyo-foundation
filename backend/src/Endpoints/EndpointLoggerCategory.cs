namespace Api.Endpoints;

/// <summary>
/// Non-static marker class for typed-logger category on foundation-owned endpoint
/// extension classes. Endpoint classes are themselves <c>static</c>, which cannot be
/// used as a generic type argument for <see cref="Microsoft.Extensions.Logging.ILogger{T}"/>.
/// Using <see cref="EndpointLoggerCategory"/> produces the log category
/// <c>Api.Endpoints.EndpointLoggerCategory</c> which is stable across products.
/// </summary>
public sealed class EndpointLoggerCategory
{
}
