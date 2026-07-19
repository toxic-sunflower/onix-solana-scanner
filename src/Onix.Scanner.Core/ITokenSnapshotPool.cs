namespace Onix.Scanner.Core;

public interface ITokenSnapshotPool
{
    ref TokenSnapshot GetSnapshot(int index);
    TokenSnapshot ReadSnapshot(int index);
    int GetOrAddIndex(Guid tokenId);
    bool TryGetIndex(Guid tokenId, out int index);
    int Count { get; }
}
