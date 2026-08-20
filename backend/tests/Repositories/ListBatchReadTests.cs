using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// The batched list reads that back the export path and the lookup validation: grouped
/// dictionaries keyed by parent id, per-group order identical to the single-parent reads, and
/// the index-aligned existence count. Committed rows via the repositories, unique names per test.
/// </summary>
[Collection("Database collection")]
public class ListBatchReadTests
{
    private readonly IListDefinitionRepository _definitions;
    private readonly IListInstanceRepository _instances;
    private readonly IResourceGroupRepository _groups;
    private readonly IResourceTypeRepository _types;

    public ListBatchReadTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _definitions = scope.ServiceProvider.GetRequiredService<IListDefinitionRepository>();
        _instances = scope.ServiceProvider.GetRequiredService<IListInstanceRepository>();
        _groups = scope.ServiceProvider.GetRequiredService<IResourceGroupRepository>();
        _types = scope.ServiceProvider.GetRequiredService<IResourceTypeRepository>();
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task<ListDefinitionInfo> CreateDefinitionWithColumnsAsync(int columns)
    {
        var definition = await _definitions.CreateAsync(
            new CreateListDefinitionRequest { Name = Unique("Batch") });
        for (var i = 0; i < columns; i++)
        {
            await _definitions.CreateColumnAsync(definition.Id, new CreateListColumnRequest
            {
                Key = $"col_{i}_{Guid.NewGuid():N}"[..20].ToLowerInvariant(),
                Label = $"Column {i}",
                DataType = ListColumnDataTypes.Text,
                SortOrder = columns - i, // reversed, so ordering is observable
            });
        }
        return definition;
    }

    [Fact]
    public async Task GetColumnsByDefinitions_GroupsAndKeepsFormOrder()
    {
        var a = await CreateDefinitionWithColumnsAsync(3);
        var b = await CreateDefinitionWithColumnsAsync(1);
        var empty = await _definitions.CreateAsync(
            new CreateListDefinitionRequest { Name = Unique("Empty") });

        var byDefinition = await _definitions.GetColumnsByDefinitionsAsync([a.Id, b.Id, empty.Id]);

        Assert.Equal(3, byDefinition[a.Id].Count);
        Assert.Single(byDefinition[b.Id]);
        Assert.False(byDefinition.ContainsKey(empty.Id));
        // Same order as the single-definition read.
        var single = await _definitions.GetColumnsAsync(a.Id);
        Assert.Equal(single.Select(c => c.Id), byDefinition[a.Id].Select(c => c.Id));
    }

    [Fact]
    public async Task GetSharedByDefinitions_GroupsByDefinition()
    {
        var a = await CreateDefinitionWithColumnsAsync(1);
        var b = await CreateDefinitionWithColumnsAsync(1);
        await _instances.CreateSharedAsync(a.Id, new CreateListInstanceRequest { Name = Unique("A1") });
        await _instances.CreateSharedAsync(a.Id, new CreateListInstanceRequest { Name = Unique("A2") });
        await _instances.CreateSharedAsync(b.Id, new CreateListInstanceRequest { Name = Unique("B1") });

        var byDefinition = await _instances.GetSharedByDefinitionsAsync([a.Id, b.Id]);

        Assert.Equal(2, byDefinition[a.Id].Count);
        Assert.Single(byDefinition[b.Id]);
        var single = await _instances.GetSharedByDefinitionAsync(a.Id);
        Assert.Equal(single.Select(i => i.Id), byDefinition[a.Id].Select(i => i.Id));
    }

    [Fact]
    public async Task GetRowsByInstances_GroupsAndKeepsInsertionOrder()
    {
        var definition = await CreateDefinitionWithColumnsAsync(1);
        var key = (await _definitions.GetColumnsAsync(definition.Id))[0].Key;
        var one = await _instances.CreateSharedAsync(definition.Id, new CreateListInstanceRequest { Name = Unique("One") });
        var two = await _instances.CreateSharedAsync(definition.Id, new CreateListInstanceRequest { Name = Unique("Two") });
        foreach (var text in new[] { "first", "second" })
            await _instances.CreateRowAsync(one.Id, Values(key, text), ListRowService.MaxRowsPerInstance);
        await _instances.CreateRowAsync(two.Id, Values(key, "only"), ListRowService.MaxRowsPerInstance);

        var byInstance = await _instances.GetRowsByInstancesAsync([one.Id, two.Id]);

        Assert.Equal(2, byInstance[one.Id].Count);
        Assert.Single(byInstance[two.Id]);
        var single = await _instances.GetRowsAsync(one.Id);
        Assert.Equal(single.Select(r => r.Id), byInstance[one.Id].Select(r => r.Id));
        Assert.Empty(await _instances.GetRowsByInstancesAsync([]));
    }

    [Fact]
    public async Task CountExistingRowsBatch_IsIndexAligned_AndScopedPerInstance()
    {
        var definition = await CreateDefinitionWithColumnsAsync(1);
        var key = (await _definitions.GetColumnsAsync(definition.Id))[0].Key;
        var one = await _instances.CreateSharedAsync(definition.Id, new CreateListInstanceRequest { Name = Unique("One") });
        var two = await _instances.CreateSharedAsync(definition.Id, new CreateListInstanceRequest { Name = Unique("Two") });
        var rowInOne = (await _instances.CreateRowAsync(one.Id, Values(key, "a"), ListRowService.MaxRowsPerInstance))!;
        var rowInTwo = (await _instances.CreateRowAsync(two.Id, Values(key, "b"), ListRowService.MaxRowsPerInstance))!;

        var found = await _instances.CountExistingRowsBatchAsync(
        [
            (one.Id, new[] { rowInOne.Id }),                    // exists in its instance → 1
            (one.Id, new[] { rowInTwo.Id }),                    // wrong instance → 0
            (two.Id, new[] { rowInTwo.Id, Guid.NewGuid() }),    // one real, one unknown → 1
            (one.Id, Array.Empty<Guid>()),                      // nothing picked → 0
        ]);

        Assert.Equal([1, 0, 1, 0], found);
        Assert.Empty(await _instances.CountExistingRowsBatchAsync([]));
    }

    [Fact]
    public async Task GetByTypeKeys_ReturnsGroupsOfEveryNamedType()
    {
        var typeA = await _types.CreateAsync(new CreateResourceTypeRequest
        {
            Key = $"batch_a_{Guid.NewGuid():N}"[..20],
            DisplayName = Unique("Type A"),
            DisplayNamePlural = Unique("Type As"),
        });
        var typeB = await _types.CreateAsync(new CreateResourceTypeRequest
        {
            Key = $"batch_b_{Guid.NewGuid():N}"[..20],
            DisplayName = Unique("Type B"),
            DisplayNamePlural = Unique("Type Bs"),
        });
        var groupA = await _groups.CreateAsync(typeA.Key, Unique("Group A"), null, 100, null, null);
        var groupB = await _groups.CreateAsync(typeB.Key, Unique("Group B"), null, 100, null, null);

        var both = await _groups.GetByTypeKeysAsync([typeA.Key, typeB.Key]);

        Assert.Contains(both, g => g.Id == groupA.Id);
        Assert.Contains(both, g => g.Id == groupB.Id);
        Assert.Empty(await _groups.GetByTypeKeysAsync([]));
    }

    private static Dictionary<string, JsonElement> Values(string key, string text) => new()
    {
        [key] = JsonSerializer.SerializeToElement(text),
    };
}
