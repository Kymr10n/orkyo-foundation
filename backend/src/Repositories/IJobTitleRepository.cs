using Api.Models;

namespace Api.Repositories;

public interface IJobTitleRepository
{
    Task<List<JobTitleInfo>> GetAllAsync(bool includeInactive = false);
    Task<JobTitleInfo?> GetByIdAsync(Guid id);
    Task<JobTitleInfo> CreateAsync(CreateJobTitleRequest request);
    Task<JobTitleInfo?> UpdateAsync(Guid id, UpdateJobTitleRequest request);
    Task<bool> DeleteAsync(Guid id);
}
