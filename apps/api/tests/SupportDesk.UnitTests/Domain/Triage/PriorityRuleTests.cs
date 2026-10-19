using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;

namespace SupportDesk.UnitTests.Domain.Triage;

public class PriorityRuleTests
{
    private static readonly Category General = new("General", requiresSpecialist: false, forcesCriticalPriority: false);
    private static readonly Category Security = new("Security", requiresSpecialist: true, forcesCriticalPriority: true);

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public void Decide_UsesTheRequestedPriority(TicketPriority requested)
    {
        var decision = PriorityRule.Decide(requested, General);

        Assert.Equal(requested, decision.Priority);
        Assert.Equal($"Requested priority {requested}.", decision.Reason);
    }

    [Fact]
    public void Decide_WithoutARequest_DefaultsToMedium()
    {
        var decision = PriorityRule.Decide(null, General);

        Assert.Equal(TicketPriority.Medium, decision.Priority);
        Assert.Equal("No priority requested; defaulted to Medium.", decision.Reason);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.High)]
    public void Decide_InACategoryThatForcesCritical_OverridesTheRequest(TicketPriority requested)
    {
        var decision = PriorityRule.Decide(requested, Security);

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Equal($"Security tickets are always Critical (requested {requested}).", decision.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TicketPriority.Critical)]
    public void Decide_InACategoryThatForcesCritical_IsCriticalWithoutMentioningARequest(TicketPriority? requested)
    {
        var decision = PriorityRule.Decide(requested, Security);

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Equal("Security tickets are always Critical.", decision.Reason);
    }

    [Fact]
    public void Decide_FollowsTheFlag_NotTheCategoryName()
    {
        var renamed = new Category("Data breach", requiresSpecialist: true, forcesCriticalPriority: true);
        var unflagged = new Category("Security", requiresSpecialist: true, forcesCriticalPriority: false);

        Assert.Equal(TicketPriority.Critical, PriorityRule.Decide(TicketPriority.Low, renamed).Priority);
        Assert.Equal(TicketPriority.Low, PriorityRule.Decide(TicketPriority.Low, unflagged).Priority);
    }
}
