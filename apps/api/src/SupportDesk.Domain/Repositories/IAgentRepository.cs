using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Triage;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Every agent with their specializations and current open-ticket count, for assignment.</summary>
    Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct);
}
