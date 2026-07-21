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
  isTracked: boolean;
  isPinned: boolean;
  spreadPct?: number | null;
  bingxAskPrice?: number | null;
  jupiterBuyPrice?: number | null;
  lastUpdated?: string | null;
}

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

type FilterType = 'all' | 'tracked' | 'untracked' | 'positive';

export default function Dashboard({ onNavigate }: Props) {
  const [allTokens, setAllTokens] = useState<TokenInfo[]>([]);
  const [connected, setConnected] = useState(false);
  const [filter, setFilter] = useState<FilterType>('all');
  const [search, setSearch] = useState('');
  const [pinningId, setPinningId] = useState<string | null>(null);
  const [ticks, setTicks] = useState<Map<string, TickPoint[]>>(new Map());
  const flashMap = useRef<Map<string, 'up' | 'down' | null>>(new Map());

  const loadAll = useCallback(async () => {
    const res = await authFetch('/api/v1/tokens/search?cexOnly=true&take=200');
    if (res.ok) {
      const data = await res.json();
      setAllTokens(data.items ?? data);
    }
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
    loadAll();
  }, [loadAll]);

  useEffect(() => {
    allTokens.forEach(t => loadTicks(t.id));
  }, [allTokens, loadTicks]);

  useEffect(() => {
    connection.on('token.quote', (p: QuotePayload) => {
      setAllTokens(prev => {
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

  const doPin = async (id: string, isPinned: boolean) => {
    setPinningId(id);
    await authFetch(`/api/v1/user-tokens/${id}/pin`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isPinned }),
    });
    setPinningId(null);
    setAllTokens(prev => prev.map(t => t.id === id ? { ...t, isPinned } : t));
  };

  const doTrack = async (id: string) => {
    await authFetch('/api/v1/user-tokens', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenId: id }),
    });
    setAllTokens(prev => prev.map(t => t.id === id ? { ...t, isTracked: true } : t));
  };

  const doUntrack = async (id: string) => {
    await authFetch(`/api/v1/user-tokens/${id}`, { method: 'DELETE' });
    setAllTokens(prev => prev.map(t => t.id === id ? { ...t, isTracked: false, isPinned: false } : t));
  };

  const filtered = useMemo(() => {
    let list = [...allTokens];
    if (search) {
      const q = search.toLowerCase();
      list = list.filter(t => t.symbol.toLowerCase().includes(q) || (t.name ?? '').toLowerCase().includes(q));
    }
    if (filter === 'tracked') list = list.filter(t => t.isTracked);
    else if (filter === 'untracked') list = list.filter(t => !t.isTracked);
    else if (filter === 'positive') list = list.filter(t => t.spreadPct != null && t.spreadPct > 0);
    return list;
  }, [allTokens, filter, search]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <div className={`w-2.5 h-2.5 rounded-full ${connected ? 'bg-[#22c55e]' : 'bg-[#f59e0b] shimmer'}`} />
          <h2 className="text-lg font-bold text-[#f1f5f9]">Dashboard</h2>
        </div>
        <div className="flex gap-2">
          <button onClick={() => onNavigate('settings')}
            className="px-3 py-1.5 bg-[#1e1f28] rounded text-sm text-[#94a3b8] hover:text-[#f59e0b] transition-colors">⚙ Settings</button>
        </div>
      </div>

      <div className="flex gap-1.5 mb-3 flex-wrap items-center">
        {(['all', 'tracked', 'untracked', 'positive'] as const).map(v => (
          <button key={v} onClick={() => setFilter(v)}
            className={`px-2.5 py-1 rounded text-xs transition-colors ${filter === v ? 'bg-[#d97706] text-black font-medium' : 'bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8]'}`}>
            {v === 'all' ? 'All' : v === 'tracked' ? 'Tracked' : v === 'untracked' ? 'Untracked' : 'Positive'}
          </button>
        ))}
        <input type="text" placeholder="Search..."
          value={search} onChange={e => setSearch(e.target.value)}
          className="ml-auto px-2.5 py-1 bg-[#16171d] border border-[#2a2b36] rounded text-xs text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] w-36" />
      </div>

      <div className="flex flex-col gap-2.5">
        {filtered.map(t => (
          <div key={t.id} className="relative">
            <div className="flex items-center gap-1.5 mb-1 px-1">
              <button
                onClick={() => t.isTracked && doPin(t.id, !t.isPinned)}
                disabled={!t.isTracked || pinningId === t.id}
                className={`text-xs transition-colors ${t.isPinned ? 'text-[#f59e0b]' : 'text-[#2a2b36] hover:text-[#64748b]'} ${!t.isTracked ? 'cursor-not-allowed' : 'cursor-pointer'}`}>
                {t.isPinned ? '★' : '☆'}
              </button>
              <span className="text-sm font-semibold text-[#f1f5f9]">{t.symbol}</span>
              {t.isTracked ? (
                <button onClick={() => doUntrack(t.id)}
                  className="ml-auto px-2 py-0.5 text-[10px] rounded bg-[#2a2b36] text-[#94a3b8] hover:text-[#ef4444] hover:bg-[#3a2a2a] transition-colors">Remove</button>
              ) : (
                <button onClick={() => doTrack(t.id)}
                  className="ml-auto px-2 py-0.5 text-[10px] font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors">+Track</button>
              )}
            </div>
            <TokenCard token={t as unknown as UserTokenDto}
              flash={flashMap.current.get(t.id) ?? null}
              ticks={ticks.get(t.id)}
              onClickChart={(id) => onNavigate('chart', id)}
              onClickHistory={(id) => onNavigate('history', id)} />
          </div>
        ))}
        {filtered.length === 0 && (
          <p className="text-sm text-[#64748b] text-center py-8">No tokens</p>
        )}
      </div>
    </div>
  );
}