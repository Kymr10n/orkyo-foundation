using Api.Models;

namespace Api.Repositories;

public interface IDepartmentRepository
{
    Task<List<DepartmentInfo>> GetAllAsync(bool includeInactive = false);
    Task<List<DepartmentTreeNode>> GetTreeAsync(bool includeInactive = false);
    Task<DepartmentInfo?> GetByIdAsync(Guid id);
    Task<DepartmentInfo> CreateAsync(CreateDepartmentRequest request);
    Task<DepartmentInfo?> UpdateAsync(Guid id, UpdateDepartmentRequest request);
    Task<bool> DeleteAsync(Guid id);
}
