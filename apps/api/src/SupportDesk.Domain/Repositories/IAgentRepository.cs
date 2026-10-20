using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Triage;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>The agent with this sign-in email, read-only, or null. Matching follows the database collation.</summary>
    Task<Agent?> GetByEmailAsync(string email, CancellationToken ct);

    /// <summary>Every agent with their specializations and current open-ticket count, for assignment.</summary>
    Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct);
}
