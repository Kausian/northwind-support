using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Access to <see cref="Category"/> aggregates.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Loads a category to read its rules from, or null when it does not exist.</summary>
    Task<Category?> GetByIdAsync(int id, CancellationToken ct);
}
