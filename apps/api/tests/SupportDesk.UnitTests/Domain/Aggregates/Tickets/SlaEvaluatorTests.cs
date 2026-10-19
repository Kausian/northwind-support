using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

/// <summary>BR-8, including the exact moments a ticket changes state.</summary>
public class SlaEvaluatorTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    /// <summary>A 4 h window with the 25% threshold: at risk for its last hour.</summary>
    private const int FourHours = 4 * 3600;
    private const double Quarter = 0.25;

    private static SlaStatus Open(DateTime due) => SlaEvaluator.Evaluate(due, FourHours, null, Now, Quarter);

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, null, null, Now, Quarter));

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable_EvenWhenResolved() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, null, Now, Now, Quarter));

    [Fact]
    public void Evaluate_ResolvedBeforeDue_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, FourHours, Now.AddMinutes(-1), Now.AddDays(1), Quarter));

    [Fact]
    public void Evaluate_ResolvedExactlyAtDue_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, FourHours, Now, Now.AddDays(1), Quarter));

    [Fact]
    public void Evaluate_ResolvedOneMillisecondLate_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now, FourHours, Now.AddMilliseconds(1), Now, Quarter));

    [Fact]
    public void Evaluate_ResolvedOnTime_StaysMet_HoweverLateItIsNow() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, FourHours, Now.AddHours(-1), Now.AddYears(1), Quarter));

    [Fact]
    public void Evaluate_OpenAndOneMillisecondPastDue_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, Open(Now.AddMilliseconds(-1)));

    [Fact]
    public void Evaluate_OpenAndExactlyAtDue_IsAtRisk_NotYetBreached() =>
        Assert.Equal(SlaStatus.AtRisk, Open(Now));

    [Fact]
    public void Evaluate_OpenWithExactlyTheThresholdLeft_IsAtRisk() =>
        Assert.Equal(SlaStatus.AtRisk, Open(Now.AddHours(1)));

    [Fact]
    public void Evaluate_OpenWithJustOverTheThresholdLeft_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, Open(Now.AddHours(1).AddMilliseconds(1)));

    [Fact]
    public void Evaluate_OpenAndFreshlyRaised_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, Open(Now.AddHours(4)));

    [Theory]
    [InlineData(10, 2 * 3600, SlaStatus.WithinSla)] // 10% of 4 h is 24 min; 2 h left
    [InlineData(10, 24 * 60, SlaStatus.AtRisk)]
    [InlineData(50, 2 * 3600, SlaStatus.AtRisk)] // 50% of 4 h is 2 h
    [InlineData(50, 2 * 3600 + 1, SlaStatus.WithinSla)]
    public void Evaluate_UsesTheConfiguredThreshold(double percent, int secondsLeft, SlaStatus expected) =>
        Assert.Equal(expected, SlaEvaluator.Evaluate(Now.AddSeconds(secondsLeft), FourHours, null, Now, percent / 100));

    [Fact]
    public void Evaluate_WindowThatDoesNotDivideEvenly_TruncatesTheLeadToWholeSeconds()
    {
        // 25% of 1001 s is 250.25 s; the lead is 250 s, exactly as SQL's CAST(... AS int) gives.
        Assert.Equal(250, SlaEvaluator.AtRiskLeadSeconds(1001, Quarter));
        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(Now.AddSeconds(250), 1001, null, Now, Quarter));
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(Now.AddSeconds(251), 1001, null, Now, Quarter));
    }
}
