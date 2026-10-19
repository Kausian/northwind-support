using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Triage;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Triage;

public class SlaPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public void WindowFor_StandardCustomer_IsTheBaseWindow(TicketPriority priority, double expectedHours)
    {
        var window = TestSla.Policy().WindowFor(priority, CustomerTier.Standard);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window.Length);
        Assert.Equal($"{priority} priority: {expectedHours} h window.", window.Reason);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 36)]
    public void WindowFor_PremiumCustomer_IsHalved(TicketPriority priority, double expectedHours)
    {
        var window = TestSla.Policy().WindowFor(priority, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window.Length);
        Assert.Contains($"reduced to {expectedHours} h for a Premium customer", window.Reason);
    }

    [Fact]
    public void WindowFor_PremiumCustomer_IsNeverShorterThanTheMinimum()
    {
        // 1.5 h halved would be 0.75 h; the 1 h floor wins.
        var policy = new SlaPolicy(TestSla.Settings(criticalHours: 1.5));

        var window = policy.WindowFor(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(1), window.Length);
        Assert.Equal(
            "Critical priority: 1.5 h window, reduced to 0.75 h for a Premium customer, raised to the 1 h minimum.",
            window.Reason);
    }

    [Fact]
    public void WindowFor_PremiumCustomer_ExactlyAtTheMinimum_IsNotRaised()
    {
        var policy = new SlaPolicy(TestSla.Settings(criticalHours: 2));

        var window = policy.WindowFor(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(1), window.Length);
        Assert.DoesNotContain("minimum", window.Reason);
    }

    [Fact]
    public void WindowFor_StandardCustomer_IsNotSubjectToThePremiumMinimum()
    {
        // BR-3 places the floor on the Premium reduction, so a configured base window stands.
        var policy = new SlaPolicy(TestSla.Settings(criticalHours: 0.5));

        var window = policy.WindowFor(TicketPriority.Critical, CustomerTier.Standard);

        Assert.Equal(TimeSpan.FromMinutes(30), window.Length);
    }

    [Fact]
    public void WindowFor_UsesTheConfiguredNumbers()
    {
        var policy = new SlaPolicy(TestSla.Settings(premiumMultiplier: 0.25, criticalHours: 8));

        Assert.Equal(TimeSpan.FromHours(8), policy.WindowFor(TicketPriority.Critical, CustomerTier.Standard).Length);
        Assert.Equal(TimeSpan.FromHours(2), policy.WindowFor(TicketPriority.Critical, CustomerTier.Premium).Length);
    }

    [Fact]
    public void DueFrom_AddsTheWindowToTheStart()
    {
        var window = TestSla.Policy().WindowFor(TicketPriority.High, CustomerTier.Standard);

        Assert.Equal(FixedClock.DefaultNow.AddHours(8), window.DueFrom(FixedClock.DefaultNow));
    }

    [Fact]
    public void Constructor_RejectsAMissingPriority()
    {
        var settings = TestSla.Settings();
        settings.BaseWindowHours.Remove(TicketPriority.Low);

        var exception = Assert.Throws<ArgumentException>(() => new SlaPolicy(settings));

        Assert.Contains("Sla:BaseWindowHours:Low", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public void Constructor_RejectsAPremiumMultiplierOutOfRange(double multiplier)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SlaPolicy(TestSla.Settings(premiumMultiplier: multiplier)));

        Assert.Contains("PremiumCustomerMultiplier", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsANonPositiveMinimum()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new SlaPolicy(TestSla.Settings(minimumWindowHours: 0)));

        Assert.Contains("MinimumWindowHours", exception.Message);
    }
}
