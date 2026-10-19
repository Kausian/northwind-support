namespace SupportDesk.Domain.Triage;

/// <summary>
/// What assignment needs to know about an agent: whether they take work, what they are
/// qualified for, and how much they already hold. A read model, loaded in one query.
/// </summary>
/// <param name="OpenTicketCount">Tickets assigned to the agent that are not Resolved or Closed.</param>
public sealed record AgentWorkload(
    int AgentId,
    string FullName,
    bool IsActive,
    int MaxOpenTickets,
    int OpenTicketCount,
    IReadOnlyCollection<int> SpecializationCategoryIds)
{
    /// <summary>Strictly below the limit: an agent at their limit takes nothing more.</summary>
    public bool HasCapacity => OpenTicketCount < MaxOpenTickets;

    public bool IsSpecialisedIn(int categoryId) => SpecializationCategoryIds.Contains(categoryId);
}
