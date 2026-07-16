using System.Collections.Concurrent;
using Onix.Scanner.Core;

namespace Onix.Scanner.Infrastructure;

public sealed class TokenSnapshotPool : ITokenSnapshotPool, IDisposable
{
    private TokenSnapshot[] _snapshots = new TokenSnapshot[64];
    private readonly ConcurrentDictionary<Guid, int> _tokenIndex = new();
    private int _count;

    public ref TokenSnapshot GetSnapshot(int index)
    {
        return ref _snapshots[index];
    }

    public int GetOrAddIndex(Guid tokenId)
    {
        if (_tokenIndex.TryGetValue(tokenId, out var index))
            return index;

        index = Interlocked.Increment(ref _count) - 1;

        if (index >= _snapshots.Length)
            Array.Resize(ref _snapshots, _snapshots.Length * 2);

        _snapshots[index].TokenId = tokenId;
        _snapshots[index].Sequence = 0;

        _tokenIndex[tokenId] = index;
        return index;
    }

    public bool TryGetIndex(Guid tokenId, out int index)
    {
        return _tokenIndex.TryGetValue(tokenId, out index);
    }

    public int Count => Volatile.Read(ref _count);

    public void Dispose()
    {
        _tokenIndex.Clear();
        _snapshots = [];
        _count = 0;
    }
}
