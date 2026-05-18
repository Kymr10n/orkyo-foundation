using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

[Collection("Database collection")]
public class DepartmentRepositoryTests
{
    private readonly IDepartmentRepository _repo;

    public DepartmentRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IDepartmentRepository>();
    }

    private static string UniqueName(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}";
        return name.Length <= 50 ? name : name[..50];
    }

    [Fact]
    public async Task Create_ThenGetById_RoundTrips()
    {
        var request = new CreateDepartmentRequest
        {
            Name = UniqueName("TestDept"),
        };
        var created = await _repo.CreateAsync(request);
        Assert.NotEqual(Guid.Empty, created.Id);

        var retrieved = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(request.Name, retrieved.Name);
        Assert.True(retrieved.IsActive);
    }

    [Fact]
    public async Task GetTree_ReturnsHierarchy()
    {
        var root = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Root")
        });
        var child = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Child"),
            ParentDepartmentId = root.Id
        });

        var tree = await _repo.GetTreeAsync();
        var rootNode = tree.SingleOrDefault(n => n.Id == root.Id);
        Assert.NotNull(rootNode);
        Assert.Single(rootNode.Children);
        Assert.Equal(child.Id, rootNode.Children[0].Id);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Update_TogglesIsActive()
    {
        var created = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("UpdateTest")
        });

        var updateRequest = new UpdateDepartmentRequest
        {
            IsActive = false,
            ChangeParent = false
        };
        await _repo.UpdateAsync(created.Id, updateRequest);

        var updated = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyActiveByDefault()
    {
        await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Active")
        });
        var inactive = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Inactive")
        });
        await _repo.UpdateAsync(inactive.Id, new UpdateDepartmentRequest { IsActive = false });

        var all = await _repo.GetAllAsync();
        Assert.All(all, d => Assert.True(d.IsActive));
    }

    [Fact]
    public async Task GetAll_IncludesInactive_WhenRequested()
    {
        // The test DB is shared across the suite, so other tests may have left
        // departments behind. Assert presence-by-id rather than exact total count.
        var active = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Active")
        });
        var inactive = await _repo.CreateAsync(new CreateDepartmentRequest
        {
            Name = UniqueName("Inactive")
        });
        await _repo.UpdateAsync(inactive.Id, new UpdateDepartmentRequest { IsActive = false });

        var all = await _repo.GetAllAsync(includeInactive: true);
        Assert.Contains(all, d => d.Id == active.Id && d.IsActive);
        Assert.Contains(all, d => d.Id == inactive.Id && !d.IsActive);
    }
}
