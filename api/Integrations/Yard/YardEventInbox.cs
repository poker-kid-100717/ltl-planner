using System.Collections.Concurrent;
using Portfolio.Ltl.Api.Models;

namespace Portfolio.Ltl.Api.Integrations.Yard;

public sealed class YardEventInbox
{
    private readonly ConcurrentDictionary<Guid, YardIntegrationEvent> events = new();
    public IReadOnlyCollection<YardIntegrationEvent> Events => events.Values.ToArray();
    public bool TryAccept(YardIntegrationEvent evt) => events.TryAdd(evt.EventId, evt);
}
