using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class CategoryRepository(SupportDbContext db) : ICategoryRepository
{
    /// <summary>Read-only: nothing changes a category, so it is not tracked.</summary>
    public Task<Category?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Category>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
}
