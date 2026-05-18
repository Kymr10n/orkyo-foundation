using System;
using System.Linq;
using System.Threading.Tasks;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

[Collection("Database collection")]
public class JobTitleRepositoryTests
{
    private readonly IJobTitleRepository _repo;

    public JobTitleRepositoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IJobTitleRepository>();
    }

    private static string UniqueName(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}";
        return name.Length <= 50 ? name : name[..50];
    }

    [Fact]
    public async Task Create_ThenGetById_RoundTrips()
    {
        var request = new CreateJobTitleRequest
        {
            Name = UniqueName("Title"),
            Description = "Test description"
        };
        var created = await _repo.CreateAsync(request);
        Assert.NotEqual(Guid.Empty, created.Id);

        var retrieved = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(request.Name, retrieved.Name);
        Assert.Equal(request.Description, retrieved.Description);
        Assert.True(retrieved.IsActive);
    }

    [Fact]
    public async Task GetAll_ExcludesInactive()
    {
        await _repo.CreateAsync(new CreateJobTitleRequest
        {
            Name = UniqueName("Active")
        });
        var toDeactivate = await _repo.CreateAsync(new CreateJobTitleRequest
        {
            Name = UniqueName("Inactive")
        });

        await _repo.UpdateAsync(toDeactivate.Id, new UpdateJobTitleRequest { IsActive = false });

        var all = await _repo.GetAllAsync();
        Assert.All(all, jt => Assert.True(jt.IsActive));
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
        var created = await _repo.CreateAsync(new CreateJobTitleRequest
        {
            Name = UniqueName("UpdateTest")
        });

        await _repo.UpdateAsync(created.Id, new UpdateJobTitleRequest
        {
            IsActive = false
        });

        var updated = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesRecord()
    {
        var created = await _repo.CreateAsync(new CreateJobTitleRequest
        {
            Name = UniqueName("DeleteTest")
        });

        var deleted = await _repo.DeleteAsync(created.Id);
        Assert.True(deleted);

        var retrieved = await _repo.GetByIdAsync(created.Id);
        Assert.Null(retrieved);
    }
}
