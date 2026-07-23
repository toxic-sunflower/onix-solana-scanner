using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Onix.Scanner.Api.Services;

public sealed class SseBroadcaster
{
    public const string PremiumGroup = "premium";
    public const string FreeGroup = "free";

    private readonly ConcurrentDictionary<Guid, (string Group, ChannelWriter<string> Writer)> _clients = new();

    public Guid Register(string group, ChannelWriter<string> writer)
    {
        var id = Guid.NewGuid();
        _clients[id] = (group, writer);
        return id;
    }

    public void Unregister(Guid id) => _clients.TryRemove(id, out _);

    public void Broadcast(string group, string eventName, object payload)
    {
        var frame = Frame(eventName, payload);
        foreach (var (group2, writer) in _clients.Values)
        {
            if (group2 != group) continue;
            writer.TryWrite(frame);
        }
    }

    public static string Frame(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return $"event: {eventName}\ndata: {json}\n\n";
    }
}
