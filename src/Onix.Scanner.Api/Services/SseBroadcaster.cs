using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Onix.Scanner.Api.Services;

public sealed class SseBroadcaster
{
    public const string PremiumGroup = "premium";
    public const string FreeGroup = "free";

    private readonly ConcurrentDictionary<Guid, (string Group, Guid UserId, ChannelWriter<string> Writer)> _clients = new();

    public Guid Register(string group, Guid userId, ChannelWriter<string> writer)
    {
        var id = Guid.NewGuid();
        _clients[id] = (group, userId, writer);
        return id;
    }

    public void Unregister(Guid id) => _clients.TryRemove(id, out _);

    public void Broadcast(string group, string eventName, object payload)
    {
        var frame = Frame(eventName, payload);
        foreach (var (group2, _, writer) in _clients.Values)
        {
            if (group2 != group) continue;
            writer.TryWrite(frame);
        }
    }

    /// <summary>Distinct user IDs with at least one open SSE connection right
    /// now — used by DemoUsageTrackerService to tick "online" seconds only
    /// for users actually receiving live data.</summary>
    public HashSet<Guid> GetOnlineUserIds()
    {
        var ids = new HashSet<Guid>();
        foreach (var (_, userId, _) in _clients.Values)
            ids.Add(userId);
        return ids;
    }

    public static string Frame(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return $"event: {eventName}\ndata: {json}\n\n";
    }
}
