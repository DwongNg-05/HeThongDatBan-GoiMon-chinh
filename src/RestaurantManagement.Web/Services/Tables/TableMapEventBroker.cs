using System.Collections.Concurrent;
using System.Threading.Channels;
using RestaurantManagement.Web.Models;

namespace RestaurantManagement.Web.Services;

/// <summary>Fans status notifications out to connected table-map clients in this web process.</summary>
public sealed class TableMapEventBroker
{
    private readonly ConcurrentDictionary<Guid, Channel<TableStatusTransition>> _subscribers = new();

    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TableStatusTransition>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return new Subscription(_subscribers, id, channel);
    }

    public void Publish(TableStatusTransition transition)
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(transition);
    }

    public sealed class Subscription(ConcurrentDictionary<Guid, Channel<TableStatusTransition>> subscribers,
        Guid id, Channel<TableStatusTransition> channel) : IDisposable
    {
        public ChannelReader<TableStatusTransition> Reader => channel.Reader;

        public void Dispose()
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
