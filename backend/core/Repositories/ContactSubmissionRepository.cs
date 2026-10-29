using Api.Models;
using Api.Services;

namespace Api.Repositories;

public interface IContactSubmissionRepository
{
    Task AddAsync(ContactRequest request, DateTime createdAtUtc, CancellationToken ct = default);
}

/// <summary>Marketing contact-form submissions, stored in the control plane.</summary>
public sealed class ContactSubmissionRepository(IDbConnectionFactory connectionFactory) : IContactSubmissionRepository
{
    public async Task AddAsync(ContactRequest request, DateTime createdAtUtc, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO contact_submissions (id, name, email, company, subject, message, created_at_utc)
            VALUES (@id, @name, @email, @company, @subject, @message, @createdAt)",
            p =>
            {
                p.AddWithValue("id", Guid.NewGuid());
                p.AddWithValue("name", request.Name.Trim());
                p.AddWithValue("email", request.Email.Trim());
                p.AddNullable("company", request.Company?.Trim());
                p.AddWithValue("subject", request.Subject);
                p.AddWithValue("message", request.Message.Trim());
                p.AddWithValue("createdAt", createdAtUtc);
            }, ct);
    }
}
