using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IRoutingRepository
{
    Task<List<RoutingInfo>> GetAllAsync(CancellationToken ct = default);
    Task<RoutingInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoutingInfo> CreateAsync(CreateRoutingRequest request, CancellationToken ct = default);
    /// <summary>Replaces the header and the whole step list. Null when the routing does not exist.</summary>
    Task<RoutingInfo?> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class RoutingRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory) : IRoutingRepository
{
    private const string SelectRoutings = @"
        SELECT id, name, description, created_at, updated_at
          FROM routings";

    private const string SelectSteps = @"
        SELECT s.id, s.routing_id, s.step_no, s.operation_template_id, t.name,
               s.setup_minutes, s.run_minutes_per_unit, s.lag_minutes_after
          FROM routing_steps s
          JOIN templates t ON t.id = s.operation_template_id";

    private sealed record Header(Guid Id, string Name, string? Description, DateTime CreatedAt, DateTime UpdatedAt);

    private static Header MapHeader(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetDateTime(3), r.GetDateTime(4));

    private static (Guid RoutingId, RoutingStepInfo Step) MapStep(NpgsqlDataReader r) => (
        r.GetGuid(1),
        new RoutingStepInfo
        {
            Id = r.GetGuid(0),
            StepNo = r.GetInt32(2),
            OperationTemplateId = r.GetGuid(3),
            OperationName = r.GetString(4),
            SetupMinutes = r.GetInt32(5),
            RunMinutesPerUnit = r.GetInt32(6),
            LagMinutesAfter = r.GetInt32(7),
        });

    private static RoutingInfo Assemble(Header h, IEnumerable<RoutingStepInfo> steps) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Description = h.Description,
        Steps = steps.OrderBy(s => s.StepNo).ToList(),
        CreatedAt = h.CreatedAt,
        UpdatedAt = h.UpdatedAt,
    };

    public async Task<List<RoutingInfo>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var headers = await conn.QueryListAsync(SelectRoutings + " ORDER BY name LIMIT 500", null, MapHeader, ct);
        if (headers.Count == 0) return [];

        var ids = headers.Select(h => h.Id).ToArray();
        var steps = await conn.QueryListAsync(
            SelectSteps + " WHERE s.routing_id = ANY(@ids) ORDER BY s.routing_id, s.step_no",
            p => p.AddWithValue("ids", ids), MapStep, ct);
        var byRouting = steps.ToLookup(s => s.RoutingId, s => s.Step);

        return headers.Select(h => Assemble(h, byRouting[h.Id])).ToList();
    }

    public async Task<RoutingInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await ReadAsync(conn, id, ct);
    }

    private static async Task<RoutingInfo?> ReadAsync(NpgsqlConnection conn, Guid id, CancellationToken ct)
    {
        var header = await conn.QuerySingleOrDefaultAsync(
            SelectRoutings + " WHERE id = @id", p => p.AddWithValue("id", id), MapHeader, ct);
        if (header is null) return null;

        var steps = await conn.QueryListAsync(
            SelectSteps + " WHERE s.routing_id = @id ORDER BY s.step_no",
            p => p.AddWithValue("id", id), MapStep, ct);
        return Assemble(header, steps.Select(s => s.Step));
    }

    public async Task<RoutingInfo> CreateAsync(CreateRoutingRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.ExecuteScalarAsync<Guid>(
            "INSERT INTO routings (name, description) VALUES (@name, @description) RETURNING id",
            p =>
            {
                p.AddWithValue("name", request.Name);
                p.AddNullable("description", request.Description);
            }, ct);
        await WriteStepsAsync(conn, id, request.Steps, ct);

        await tx.CommitAsync(ct);
        return (await ReadAsync(conn, id, ct))!;
    }

    public async Task<RoutingInfo?> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var updated = await conn.ExecuteAsync(
            "UPDATE routings SET name = @name, description = @description WHERE id = @id",
            p =>
            {
                p.AddWithValue("id", id);
                p.AddWithValue("name", request.Name);
                p.AddNullable("description", request.Description);
            }, ct);
        if (updated == 0) return null;

        // The step list is the routing; it is replaced as a whole rather than diffed.
        await conn.ExecuteAsync("DELETE FROM routing_steps WHERE routing_id = @id",
            p => p.AddWithValue("id", id), ct);
        await WriteStepsAsync(conn, id, request.Steps, ct);

        await tx.CommitAsync(ct);
        return await ReadAsync(conn, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM routings WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    private static async Task WriteStepsAsync(
        NpgsqlConnection conn, Guid routingId, IReadOnlyList<RoutingStepRequest> steps, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO routing_steps
                    (routing_id, step_no, operation_template_id, setup_minutes, run_minutes_per_unit, lag_minutes_after)
                VALUES (@routingId, @stepNo, @templateId, @setup, @run, @lag)",
                p =>
                {
                    p.AddWithValue("routingId", routingId);
                    p.AddWithValue("stepNo", step.StepNo);
                    p.AddWithValue("templateId", step.OperationTemplateId);
                    p.AddWithValue("setup", step.SetupMinutes);
                    p.AddWithValue("run", step.RunMinutesPerUnit);
                    p.AddWithValue("lag", step.LagMinutesAfter);
                }, ct);
        }
    }
}
