import { useEffect, useState, useMemo, useRef, useCallback } from 'react';
import type { UserTokenDto, QuotePayload, TickPoint } from '../types';
import { authFetch } from '../lib/auth';
import connection, { startConnection } from '../lib/signalr';
import TokenCard from './TokenCard';

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  isAvailableOnCex: boolean;
  spreadPct?: number | null;
}

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

type SortKey = 'spread' | 'symbol' | 'updated' | 'hasspread';
interface SortCriterion { key: SortKey; dir: 'asc' | 'desc'; }

const sortLabels: Record<SortKey, string> = {
  spread: 'Spread',
  symbol: 'A–Z',
  updated: 'Recent',
  hasspread: 'Has spread',
};

export default function Dashboard({ onNavigate }: Props) {
  const [tokens, setTokens] = useState<UserTokenDto[]>([]);
  const [addSearch, setAddSearch] = useState('');
  const [addResults, setAddResults] = useState<TokenInfo[]>([]);
  const [addingId, setAddingId] = useState<string | null>(null);
  const [addFocused, setAddFocused] = useState(false);
  const addBlurTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const [sorts, setSorts] = useState<SortCriterion[]>([{ key: 'hasspread', dir: 'desc' }, { key: 'spread', dir: 'desc' }]);
  const [connected, setConnected] = useState(false);
  const [ticks, setTicks] = useState<Map<string, TickPoint[]>>(new Map());
  const flashMap = useRef<Map<string, 'up' | 'down' | null>>(new Map());

  const loadTokens = useCallback(() => {
    authFetch('/api/v1/user-tokens')
      .then(res => res.ok ? res.json() : [])
      .then(setTokens)
      .catch(console.error);
  }, []);

  const loadTicks = useCallback((tokenId: string) => {
    authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=100`)
      .then(res => res.ok ? res.json() : [])
      .then((data: TickPoint[]) => {
        setTicks(prev => { const m = new Map(prev); m.set(tokenId, data); return m; });
      })
      .catch(() => {});
  }, []);

  useEffect(() => {
    loadTokens();
  }, [loadTokens]);

  useEffect(() => {
    tokens.forEach(t => loadTicks(t.id));
  }, [tokens, loadTicks]);

  useEffect(() => {
    connection.on('token.quote', (p: QuotePayload) => {
      setTokens(prev => {
        const idx = prev.findIndex(t => t.id === p.token_id);
        if (idx < 0) return prev;
        const existing = prev[idx];
        let cls: 'up' | 'down' | null = null;
        if (existing.bingxAskPrice && p.bingx_ask_price) {
          cls = p.bingx_ask_price > existing.bingxAskPrice ? 'up' : 'down';
        }
        flashMap.current.set(p.token_id, cls);
        const next = [...prev];
        next[idx] = {
          ...next[idx],
          bingxAskPrice: p.bingx_ask_price ?? 0,
          jupiterBuyPrice: p.jupiter_buy_price ?? 0,
          spreadPct: p.spread_pct ?? 0,
          lastUpdated: p.calculated_at,
        };
        return next;
      });
    });

    startConnection().then(() => setConnected(true));

    connection.onclose(() => setConnected(false));
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));

    return () => { connection.off('token.quote'); connection.off('token.status'); };
  }, []);

  const fetchAdd = useCallback(async (q: string) => {
    const res = await authFetch(`/api/v1/tokens/search?q=${encodeURIComponent(q)}&cexOnly=true&limit=200`);
    if (res.ok) setAddResults(await res.json());
  }, []);

  useEffect(() => {
    const t = setTimeout(() => fetchAdd(addSearch), 150);
    return () => clearTimeout(t);
  }, [addSearch, fetchAdd]);

  const doAddToken = async (id: string) => {
    setAddingId(id);
    await authFetch('/api/v1/user-tokens', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenId: id }),
    });
    setAddingId(null);
    setAddSearch('');
    setAddResults([]);
    loadTokens();
  };

  const spreadColor = (pct: number | null | undefined) => {
    if (pct == null) return 'text-[#64748b]';
    if (pct > 5) return 'text-[#22c55e]';
    if (pct > 2) return 'text-[#84cc16]';
    if (pct > 0) return 'text-[#eab308]';
    if (pct < -2) return 'text-[#ef4444]';
    if (pct < 0) return 'text-[#f97316]';
    return 'text-[#64748b]';
  };

  const filtered = useMemo(() => {
    let result = [...tokens];

    result.sort((a, b) => {
      for (const s of sorts) {
        let cmp = 0;
        if (s.key === 'spread') cmp = (a.spreadPct ?? 0) - (b.spreadPct ?? 0);
        else if (s.key === 'symbol') cmp = a.symbol.localeCompare(b.symbol);
        else if (s.key === 'updated') cmp = (a.lastUpdated ?? '').localeCompare(b.lastUpdated ?? '');
        else if (s.key === 'hasspread') {
          const aHas = Math.abs(a.spreadPct ?? 0) > 0.001 ? 1 : 0;
          const bHas = Math.abs(b.spreadPct ?? 0) > 0.001 ? 1 : 0;
          cmp = aHas - bHas;
        }
        if (cmp !== 0) return s.dir === 'asc' ? cmp : -cmp;
      }
      return 0;
    });

    return result;
  }, [tokens, sorts]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <div className={`w-2.5 h-2.5 rounded-full ${connected ? 'bg-[#22c55e]' : 'bg-[#f59e0b] shimmer'}`} />
          <h2 className="text-lg font-bold text-[#f1f5f9]">Dashboard</h2>
        </div>
        <div className="flex gap-2" />
      </div>

      <div className="flex gap-1.5 mb-4 flex-wrap items-center">
        {(Object.entries(sortLabels) as [SortKey, string][]).map(([key, label]) => {
          const active = sorts.find(s => s.key === key);
          return (
            <button key={key} onClick={() => {
              if (active) {
                if (active.dir === 'asc') setSorts(prev => prev.map(s => s.key === key ? { ...s, dir: 'desc' } : s));
                else setSorts(prev => prev.filter(s => s.key !== key));
              } else setSorts(prev => [...prev, { key, dir: 'asc' }]);
            }}
              className={`px-2.5 py-1 rounded text-xs transition-colors ${active ? (active.dir === 'desc' ? 'bg-[#d97706] text-black' : 'bg-[#1e1f28] text-[#f59e0b] border border-[#f59e0b]') : 'bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8]'}`}>
              {label} {active ? (active.dir === 'desc' ? '↓' : '↑') : ''}
            </button>
          );
        })}
        {sorts.length > 1 && (
          <button onClick={() => setSorts([{ key: 'hasspread', dir: 'desc' }, { key: 'spread', dir: 'desc' }])}
            className="px-2 py-1 rounded text-xs bg-[#1e1f28] text-[#ef4444] hover:bg-[#2a2b36] hover:text-[#f87171] transition-colors border border-[#ef4444]/30">✕ reset</button>
        )}
        <span className="text-xs text-[#64748b] ml-auto">{tokens.length} token{tokens.length !== 1 ? 's' : ''}</span>
      </div>

      <div className="relative mb-4">
        <input type="text" placeholder="Add token..."
          value={addSearch} onChange={e => setAddSearch(e.target.value)}
          onFocus={() => { clearTimeout(addBlurTimer.current); setAddFocused(true); if (!addSearch) fetchAdd(''); }}
          onBlur={() => { addBlurTimer.current = setTimeout(() => setAddFocused(false), 200); }}
          className="w-full px-3 py-2 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] transition-colors" />
        {addFocused && (
          <div className="absolute left-0 right-0 top-full mt-1 bg-[#1e1f28] border border-[#2a2b36] rounded-lg shadow-xl z-50 max-h-72 overflow-y-auto">
            {addResults.length === 0 && (
              <p className="text-xs text-[#64748b] text-center py-4">No tokens found</p>
            )}
            {addResults.map(t => {
              const tracked = tokens.some(mt => mt.id === t.id);
              return (
                <div key={t.id}
                  className="flex items-center justify-between px-3 py-2 border-b border-[#2a2b36] last:border-0 hover:bg-[#2a2b36] transition-colors">
                  <div className="flex items-center gap-2.5">
                    <span className="text-sm font-semibold text-[#f1f5f9]">{t.symbol}</span>
                    {t.spreadPct != null && t.spreadPct > 0 && (
                      <span className={`text-xs font-medium ${spreadColor(t.spreadPct)}`}>
                        {t.spreadPct > 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                      </span>
                    )}
                  </div>
                  {tracked ? (
                    <span className="text-xs text-[#22c55e] font-medium">✓ Tracked</span>
                  ) : (
                    <button onClick={() => doAddToken(t.id)}
                      className="px-2.5 py-1 text-xs font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors whitespace-nowrap">
                      {addingId === t.id ? '...' : '+Add'}
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {tokens.length === 0 && (
        <div className="text-center mt-8">
          <div className="text-4xl mb-3 text-[#2a2b36]">⟐</div>
          <p className="text-[#64748b] mb-1">No tokens tracked yet</p>
          <p className="text-xs text-[#475569]">Search and add tokens above</p>
        </div>
      )}

      <div className="flex flex-col gap-2.5">
        {filtered.map(t => (
          <TokenCard key={t.id} token={t}
            flash={flashMap.current.get(t.id) ?? null}
            ticks={ticks.get(t.id)}
            onClickChart={(id) => onNavigate('chart', id)}
            onClickHistory={(id) => onNavigate('history', id)} />
        ))}
      </div>

    </div>
  );
}
