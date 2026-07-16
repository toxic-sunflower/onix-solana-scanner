using Onix.Scanner.Shared.Dtos;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Core.Contracts;

public interface ISpreadTickRepository
{
    Task WriteBatchAsync(IReadOnlyList<SpreadTick> ticks, CancellationToken ct = default);
    Task<ChartResponseDto> GetChartAsync(Guid tokenId, string interval, DateTime from, DateTime to, string timezone = "UTC", CancellationToken ct = default);
    Task CleanupOldTicksAsync(CancellationToken ct = default);
}
