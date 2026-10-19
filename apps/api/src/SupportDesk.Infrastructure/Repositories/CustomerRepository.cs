using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class CustomerRepository(SupportDbContext db) : ICustomerRepository
{
    /// <summary>Read-only: nothing changes a customer, so it is not tracked.</summary>
    public Task<Customer?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
}
