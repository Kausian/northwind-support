using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Access to <see cref="Customer"/> aggregates.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>Loads a customer to read their tier from, or null when they do not exist.</summary>
    Task<Customer?> GetByIdAsync(int id, CancellationToken ct);
}
