namespace Api.Models;

public interface ISiteRequest
{
    string Code { get; }
    string Name { get; }
}

public record CreateSiteRequest(string Code, string Name, string? Description, string? Address) : ISiteRequest;

public record UpdateSiteRequest(string Code, string Name, string? Description, string? Address) : ISiteRequest;
