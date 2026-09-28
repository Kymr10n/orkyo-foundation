using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// The per-instance row cap under concurrent writers: a count inside the INSERT alone lets two
/// READ COMMITTED writers both see room, so the cap must be serialized on the instance row.
/// </summary>
[Collection("Database collection")]
public class ListRowCapTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task CreateRow_ConcurrentWritersAtCapMinusOne_ExactlyOneSucceeds()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IListDefinitionRepository>();
        var instances = scope.ServiceProvider.GetRequiredService<IListInstanceRepository>();
        var definition = await definitions.CreateAsync(
            new CreateListDefinitionRequest { Name = $"Cap {Guid.NewGuid():N}" });
        var instance = await instances.CreateSharedAsync(
            definition.Id, new CreateListInstanceRequest { Name = $"Cap {Guid.NewGuid():N}" });
        var values = new Dictionary<string, JsonElement>();
        const int cap = 2;
        (await instances.CreateRowAsync(instance.Id, values, cap)).Should().NotBeNull();

        // Writer one, on its own connection: takes the instance lock and inserts the last row
        // the cap allows, then holds its transaction open.
        await using var conn = new NpgsqlConnection(fixture.TenantConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT 1 FROM list_instances WHERE id = @id FOR UPDATE", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("id", instance.Id);
            await lockCmd.ExecuteNonQueryAsync();
        }
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO list_rows (list_instance_id, values) VALUES (@id, '{}'::jsonb)", conn, tx))
        {
            insert.Parameters.AddWithValue("id", instance.Id);
            await insert.ExecuteNonQueryAsync();
        }

        // Writer two, through the repository, while writer one is still open.
        var second = instances.CreateRowAsync(instance.Id, values, cap);
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)));
        await tx.CommitAsync();

        (await second).Should().BeNull("writer one took the last slot, so writer two must see the cap");
        (await instances.GetRowsAsync(instance.Id)).Should().HaveCount(cap);
    }
}
