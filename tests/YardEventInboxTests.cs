using Portfolio.Ltl.Api.Integrations.Yard;
using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Tests;

public sealed class YardEventInboxTests
{
    [Fact]
    public void DuplicateEventIdIsAcceptedOnlyOnce()
    {
        var inbox = new YardEventInbox();
        var evt = new YardIntegrationEvent(
            Guid.NewGuid(), "TrailerReadyForPlanning", "TRL-DEMO", DateTimeOffset.UtcNow, "Ready");

        Assert.True(inbox.TryAccept(evt));
        Assert.False(inbox.TryAccept(evt));
        Assert.Single(inbox.Events);
    }
}
