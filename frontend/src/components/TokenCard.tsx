import { useEffect, useState, useRef } from 'react';
import type { UserTokenDto, TickPoint } from '../types';

interface Props {
  token: UserTokenDto;
  flash: 'up' | 'down' | null;
  ticks?: TickPoint[];
  onClickChart: (id: string) => void;
  onClickHistory: (id: string) => void;
  isPinned?: boolean;
  onPin?: (tokenId: string, isPinned: boolean) => void;
}

function ago(utcStr?: string): string {
  if (!utcStr) return '';
  const ms = Date.now() - new Date(utcStr).getTime();
  if (ms < 0) return 'now';
  const sec = Math.floor(ms / 1000);
  if (sec < 60) return `${sec}s`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m`;
  return `${Math.floor(min / 60)}h`;
}

export default function TokenCard({ token, flash, ticks, onClickChart, onClickHistory, isPinned, onPin }: Props) {
  const hasBingx = (token.bingxAskPrice ?? 0) > 0;
  const hasJupiter = (token.jupiterBuyPrice ?? 0) > 0;
  const hasBoth = hasBingx && hasJupiter;
  const [flashClass, setFlashClass] = useState('');
  const [now, setNow] = useState(Date.now());
  const prevSpread = useRef(token.spreadPct);

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    if (flash === 'up') setFlashClass('price-up');
    else if (flash === 'down') setFlashClass('price-down');
    else if (token.spreadPct !== prevSpread.current && token.spreadPct > 0) {
      setFlashClass(token.spreadPct > prevSpread.current ? 'price-up' : 'price-down');
    }
    prevSpread.current = token.spreadPct;
    const t = setTimeout(() => setFlashClass(''), 1000);
    return () => clearTimeout(t);
  }, [token.spreadPct, token.bingxAskPrice, flash]);

  const spreadAbs = Math.abs(token.spreadPct ?? 0);
  const updatedTxt = ago(token.lastUpdated);
  const spreadDir = flash === 'up' ? '↑' : flash === 'down' ? '↓' : (token.spreadPct ?? 0) > 0 ? '↗' : '';
  void now;

  const recentLog = ticks?.slice(0, 6) ?? [];

  return (
    <div className={`bg-[#16171d] rounded-lg border border-[#2a2b36] p-3.5 flex flex-col gap-2.5 slide-in ${flashClass} group`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <span className={`w-2.5 h-2.5 rounded-full ${hasBoth ? 'bg-[#22c55e]' : hasBingx || hasJupiter ? 'bg-[#f59e0b] shimmer' : 'bg-[#64748b]'}`} />
          <span className="font-bold text-base text-[#f1f5f9]">{token.symbol}</span>
          {token.name && <span className="text-xs text-[#64748b] hidden sm:inline">{token.name}</span>}
          {token.solanaMint && <span className="text-[10px] text-[#475569] font-mono hidden md:inline truncate max-w-[140px]" title={token.solanaMint}>{token.solanaMint}</span>}
          {updatedTxt && <span className="text-sm text-[#475569] tabular-nums">{updatedTxt}</span>}
        </div>
        <div className="flex items-center gap-2">
          {onPin && (
            <button onClick={() => onPin(token.id, !isPinned)}
              title={isPinned ? 'Unpin' : 'Pin'}
              className={`text-sm transition-all ${isPinned ? 'text-[#f59e0b] opacity-100' : 'opacity-0 group-hover:opacity-40 hover:opacity-100 text-[#64748b]'}`}>
              📌
            </button>
          )}
          <span className={`text-2xl font-bold font-mono tabular-nums tracking-tight ${spreadAbs > 0 ? (token.spreadPct > 0 ? 'text-[#22c55e]' : 'text-[#ef4444]') : 'text-[#64748b]'}`}>
            {spreadAbs > 0 ? `${token.spreadPct >= 0 ? '+' : ''}${token.spreadPct.toFixed(2)}%` : '---'}
            {spreadDir && <span className="text-3xl ml-1">{spreadDir}</span>}
          </span>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2 text-sm">
        <div className="bg-[#1a1b24] rounded px-2.5 py-1.5">
          <div className="flex items-center justify-between">
            <span className="text-[#94a3b8] text-xs">CEX Bid</span>
            {hasBingx && <span className={`text-lg ${flash === 'up' ? 'text-[#22c55e]' : flash === 'down' ? 'text-[#ef4444]' : 'text-[#475569]'}`}>{flash === 'up' ? '↑' : flash === 'down' ? '↓' : '─'}</span>}
          </div>
          <div className="font-mono text-[#f1f5f9] mt-0.5 text-sm">
            {hasBingx ? `$${Number(token.bingxAskPrice).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 8 })}` : '---'}
          </div>
        </div>
        <div className="bg-[#1a1b24] rounded px-2.5 py-1.5">
          <div className="flex items-center justify-between">
            <span className="text-[#94a3b8] text-xs">DEX Ask</span>
            {hasJupiter && <span className={`text-lg ${flash === 'up' ? 'text-[#22c55e]' : flash === 'down' ? 'text-[#ef4444]' : 'text-[#475569]'}`}>{flash === 'up' ? '↑' : flash === 'down' ? '↓' : '─'}</span>}
          </div>
          <div className="font-mono text-[#f1f5f9] mt-0.5 text-sm">
            {hasJupiter ? `$${Number(token.jupiterBuyPrice).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 8 })}` : '---'}
          </div>
        </div>
      </div>

      <div className="flex gap-1.5 mt-0.5 items-center">
        {token.bingxUrl && (
          <a href={token.bingxUrl} target="_blank" rel="noreferrer"
            className="text-xs px-2 py-1 rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] hover:bg-[#2a2b36] transition-colors">BingX</a>
        )}
        {token.jupiterUrl && (
          <a href={token.jupiterUrl} target="_blank" rel="noreferrer"
            className="text-xs px-2 py-1 rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] hover:bg-[#2a2b36] transition-colors">Jupiter</a>
        )}
        {token.solscanUrl && (
          <a href={token.solscanUrl} target="_blank" rel="noreferrer"
            className="text-xs px-2 py-1 rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] hover:bg-[#2a2b36] transition-colors">Contract</a>
        )}
        <span className="w-px h-4 bg-[#2a2b36] mx-1" />
        <button onClick={() => onClickHistory(token.id)}
          className="text-xs px-2 py-1 rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] hover:bg-[#2a2b36] transition-colors">Log</button>
        <button onClick={() => onClickChart(token.id)}
          className="text-xs px-2 py-1 rounded bg-[#d97706] text-black font-medium hover:bg-[#b45309] transition-colors">Chart</button>
      </div>

      {recentLog.length > 0 && (
        <details className="bg-[#1a1b24] rounded px-2.5 py-1.5 mt-0.5 group">
          <summary className="text-[10px] text-[#475569] cursor-pointer hover:text-[#64748b] list-none flex items-center gap-1 select-none">
            <span className="text-[#475569] group-open:rotate-90 transition-transform">▶</span>
            recent ({recentLog.length})
          </summary>
          <div className="mt-1">
            {recentLog.map((t, i) => (
              <div key={i} className="flex gap-2 font-mono text-[10px] leading-4">
                <span className="text-[#475569] w-12 shrink-0">{new Date(t.time).toLocaleTimeString()}</span>
                <span className={t.spreadPct >= 0 ? 'text-[#22c55e]' : 'text-[#ef4444]'}>
                  {t.spreadPct >= 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                </span>
              </div>
            ))}
          </div>
        </details>
      )}
    </div>
  );
}
