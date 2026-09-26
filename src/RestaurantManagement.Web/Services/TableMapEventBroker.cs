using System.Collections.Concurrent;
using System.Threading.Channels;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

public sealed record TableStatusChangedEvent(string Code, string Area, int Capacity, string Status, string StatusLabel, string StatusClass, DateTimeOffset ChangedAtUtc);

/// <summary>Fans status notifications out to connected table-map clients in this web process.</summary>
public sealed class TableMapEventBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<TableStatusChangedEvent>> _subscribers = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TableStatusChangedEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new Subscription(_subscribers, id, channel);
    }

    public void Publish(DiningTableCard table)
    {
        var message = new TableStatusChangedEvent(table.Code, table.Area, table.Capacity, table.Status,
            table.StatusLabel, table.StatusClass, table.ChangedAtUtc);
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(message);
    }

    public sealed class Subscription(ConcurrentDictionary<Guid, Channel<TableStatusChangedEvent>> subscribers,
        Guid id, Channel<TableStatusChangedEvent> channel) : IDisposable
    {
        public ChannelReader<TableStatusChangedEvent> Reader => channel.Reader;

        public void Dispose()
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
