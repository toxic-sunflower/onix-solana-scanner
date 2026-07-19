import { useEffect, useState } from 'react';
import type { TickPoint, UserTokenDto } from '../types';
import { authFetch } from '../lib/auth';
import connection from '../lib/signalr';

interface Props {
  tokenId: string;
  onBack: () => void;
}

const intervals = [
  { label: '1m', sec: 60 },
  { label: '5m', sec: 300 },
  { label: '15m', sec: 900 },
  { label: '1h', sec: 3600 },
  { label: 'all', sec: 0 },
] as const;

export default function HistoryPage({ tokenId, onBack }: Props) {
  const [token, setToken] = useState<UserTokenDto | null>(null);
  const [ticks, setTicks] = useState<TickPoint[]>([]);
  const [limit, setLimit] = useState(50);
  const [intervalSec, setIntervalSec] = useState(0);

  useEffect(() => {
    fetch(`/api/v1/tokens/${tokenId}`)
      .then(res => res.json())
      .then((data: any) => setToken(data as UserTokenDto));

    connection.on('token.quote', (p: any) => {
      if (p.token_id === tokenId) {
        setToken(prev => prev ? { ...prev, spreadPct: p.spread_pct, lastUpdated: p.calculated_at } : prev);
      }
    });

    return () => { connection.off('token.quote'); };
  }, [tokenId]);

  useEffect(() => {
    authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=${limit}`)
      .then(res => res.ok ? res.json() : [])
      .then(setTicks)
      .catch(() => {});
  }, [tokenId, limit]);

  const filtered = intervalSec > 0
    ? ticks.filter(t => Date.now() - new Date(t.time).getTime() < intervalSec * 1000)
    : ticks;

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <button onClick={onBack} className="mb-4 px-3 py-1 bg-[#1e1f28] rounded text-sm text-[#94a3b8] hover:text-[#f59e0b] transition-colors">← Dashboard</button>

      {token && (
        <div className="mb-4">
          <h2 className="text-xl font-bold text-[#f1f5f9]">{token.symbol} <span className="text-sm font-normal text-[#64748b]">Log</span></h2>
          <div className="flex gap-4 text-sm text-[#94a3b8]">
            <span>Current spread: <strong className={token.spreadPct >= 0 ? 'text-[#22c55e]' : 'text-[#ef4444]'}>{token.spreadPct?.toFixed(4)}%</strong></span>
            <span>{ticks.length} entries</span>
          </div>
        </div>
      )}

      <div className="flex gap-2 mb-3 flex-wrap items-center">
        <span className="text-xs text-[#64748b]">Range:</span>
        {intervals.map(i => (
          <button key={i.label} onClick={() => setIntervalSec(i.sec)}
            className={`px-2.5 py-1 rounded text-xs ${intervalSec === i.sec ? 'bg-[#d97706] text-black' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>{i.label}</button>
        ))}
        <span className="text-xs text-[#64748b] ml-2">Limit:</span>
        {[20, 50, 100, 200].map(n => (
          <button key={n} onClick={() => setLimit(n)}
            className={`px-2 py-1 rounded text-xs ${limit === n ? 'bg-[#1e1f28] text-[#f1f5f9]' : 'bg-transparent text-[#64748b] hover:text-[#94a3b8]'}`}>{n}</button>
        ))}
      </div>

      <div className="bg-[#16171d] border border-[#2a2b36] rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-xs font-mono">
            <thead>
              <tr className="border-b border-[#2a2b36] bg-[#1a1b24]">
                <th className="text-left px-3 py-2 text-[#64748b] font-medium">Time</th>
                <th className="text-right px-3 py-2 text-[#64748b] font-medium">Spread</th>
                <th className="text-right px-3 py-2 text-[#64748b] font-medium">CEX Price</th>
                <th className="text-right px-3 py-2 text-[#64748b] font-medium">DEX Price</th>
                <th className="text-right px-3 py-2 text-[#64748b] font-medium">Direction</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((t, i) => (
                <tr key={i} className="border-b border-[#1e1f28] hover:bg-[#1a1b24] transition-colors">
                  <td className="px-3 py-1.5 text-[#475569] whitespace-nowrap">
                    {new Date(t.time).toLocaleString()}
                  </td>
                  <td className={`px-3 py-1.5 text-right tabular-nums ${t.spreadPct >= 0 ? 'text-[#22c55e]' : 'text-[#ef4444]'}`}>
                    {t.spreadPct >= 0 ? '+' : ''}{t.spreadPct.toFixed(4)}%
                  </td>
                  <td className="px-3 py-1.5 text-right text-[#f1f5f9] tabular-nums">
                    ${t.bingxPrice.toFixed(6)}
                  </td>
                  <td className="px-3 py-1.5 text-right text-[#f1f5f9] tabular-nums">
                    ${t.jupiterPrice.toFixed(6)}
                  </td>
                  <td className="px-3 py-1.5 text-right">
                    {t.spreadPct > 0.01
                      ? <span className="text-[#22c55e]">CEX ↑</span>
                      : t.spreadPct < -0.01
                        ? <span className="text-[#ef4444]">DEX ↑</span>
                        : <span className="text-[#64748b]">—</span>}
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr><td colSpan={5} className="text-center py-8 text-[#64748b]">No data</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {filtered.length > 0 && (
        <div className="flex justify-end mt-2">
          <button onClick={() => authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=${limit}`)
            .then(r => r.ok ? r.json() : [])
            .then(setTicks)}
            className="px-3 py-1 text-xs bg-[#1e1f28] text-[#94a3b8] rounded hover:bg-[#2a2b36] hover:text-[#f1f5f9] transition-colors">Refresh</button>
        </div>
      )}
    </div>
  );
}
