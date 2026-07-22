using System.Runtime.InteropServices;

namespace Onix.Scanner.Core;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TokenSnapshot
{
    public Guid TokenId;
    public long BingxAskPriceRaw;
    public long JupiterBuyPriceRaw;
    public long BingxTimestampUtc;
    public long BingxExchangeTimestampUtc;
    public int BingxLatencyMs;
    public long JupiterTimestampUtc;
    public int JupiterLatencyMs;
    public Guid? ProxyId;
    public long ProxyErrorUntilUtc;
    public long Sequence;
}
