using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Triage;

public class AgentSelectorTests
{
    private const int GeneralId = 1;
    private const int BillingId = 2;

    private static readonly Category General =
        new Category("General", requiresSpecialist: false, forcesCriticalPriority: false).WithId(GeneralId);

    private static readonly Category Billing =
        new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(BillingId);

    private static AgentWorkload Agent(
        int id, int open, int max = 10, bool active = true, params int[] specializations) =>
        new(id, $"Agent {id}", active, max, open, specializations);

    [Fact]
    public void Select_PicksTheEligibleAgentWithFewestOpenTickets()
    {
        var selection = AgentSelector.Select([Agent(1, open: 5), Agent(2, open: 2), Agent(3, open: 4)], General);

        Assert.Equal(2, selection.Agent?.AgentId);
        Assert.Equal("Assigned to Agent 2: fewest open tickets (2 of 10) among 3 eligible agents.", selection.Reason);
    }

    [Fact]
    public void Select_ForASpecialistCategory_OnlyConsidersSpecialists()
    {
        var selection = AgentSelector.Select(
            [Agent(1, open: 0, specializations: GeneralId), Agent(2, open: 3, specializations: BillingId)],
            Billing);

        Assert.Equal(2, selection.Agent?.AgentId);
        Assert.Contains("among 1 eligible agent with the Billing specialization", selection.Reason);
    }

    [Fact]
    public void Select_ForAnOpenCategory_ConsidersAgentsWithoutThatSpecialization()
    {
        var selection = AgentSelector.Select([Agent(1, open: 1, specializations: BillingId)], General);

        Assert.Equal(1, selection.Agent?.AgentId);
    }

    [Fact]
    public void Select_SkipsAnAgentAtTheirLimit()
    {
        var selection = AgentSelector.Select([Agent(1, open: 6, max: 6), Agent(2, open: 7, max: 8)], General);

        Assert.Equal(2, selection.Agent?.AgentId);
    }

    [Fact]
    public void Select_TakesAnAgentOneBelowTheirLimit()
    {
        var selection = AgentSelector.Select([Agent(1, open: 5, max: 6)], General);

        Assert.Equal(1, selection.Agent?.AgentId);
    }

    [Fact]
    public void Select_SkipsAnInactiveAgent_EvenWithTheFewestTickets()
    {
        var selection = AgentSelector.Select([Agent(1, open: 0, active: false), Agent(2, open: 4)], General);

        Assert.Equal(2, selection.Agent?.AgentId);
    }

    [Fact]
    public void Select_BreaksATieByLowestAgentId_WhateverTheInputOrder()
    {
        var selection = AgentSelector.Select([Agent(7, open: 2), Agent(3, open: 2), Agent(5, open: 2)], General);

        Assert.Equal(3, selection.Agent?.AgentId);
        Assert.EndsWith(", tie broken by lowest agent id.", selection.Reason);
    }

    [Fact]
    public void Select_WhenNobodyIsEligible_ReturnsNoAgentWithAReason()
    {
        var selection = AgentSelector.Select(
            [Agent(1, open: 0, active: false, specializations: BillingId), Agent(2, open: 6, max: 6, specializations: BillingId), Agent(3, open: 0)],
            Billing);

        Assert.Null(selection.Agent);
        Assert.Equal("Unassigned: no active Billing specialist is below their open-ticket limit.", selection.Reason);
    }

    [Fact]
    public void Select_WithNoAgentsAtAll_ReturnsNoAgent()
    {
        var selection = AgentSelector.Select([], General);

        Assert.Null(selection.Agent);
        Assert.Equal("Unassigned: no active agent is below their open-ticket limit.", selection.Reason);
    }
}
