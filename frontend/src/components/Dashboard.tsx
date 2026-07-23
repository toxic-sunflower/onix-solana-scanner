import { useEffect, useState, useMemo, useRef, useCallback } from 'react';
import type { UserTokenDto, QuotePayload, TickPoint } from '../types';
import { authFetch } from '../lib/auth';
import { on, off, onConnectionChange, startConnection } from '../lib/sse';
import TokenCard from './TokenCard';

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  solanaMint?: string;
  isPinned: boolean;
  isFavorite: boolean;
  spreadPct?: number | null;
  bingxAskPrice?: number | null;
  jupiterBuyPrice?: number | null;
  lastUpdated?: string | null;
  bingxUrl?: string;
  jupiterUrl?: string;
  solscanUrl?: string;
}

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

type FilterType = 'all' | 'positive';

export default function Dashboard({ onNavigate }: Props) {
  const [allTokens, setAllTokens] = useState<TokenInfo[]>([]);
  const [connected, setConnected] = useState(false);
  const [filter, setFilter] = useState<FilterType>('all');
  const [search, setSearch] = useState('');
  const [now, setNow] = useState(Date.now());
  const [ticks, setTicks] = useState<Map<string, TickPoint[]>>(new Map());
  const flashMap = useRef<Map<string, 'up' | 'down' | null>>(new Map());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);

  function ago(utcStr?: string): string {
    if (!utcStr) return '—';
    const ms = Date.now() - new Date(utcStr).getTime();
    if (ms < 0) return 'now';
    const sec = Math.floor(ms / 1000);
    if (sec < 60) return `${sec}s`;
    const min = Math.floor(sec / 60);
    if (min < 60) return `${min}m`;
    return `${Math.floor(min / 60)}h`;
  }

  const latestUpdate = useMemo(() => {
    let latest: string | null = null;
    for (const t of allTokens) {
      if (t.lastUpdated && (!latest || t.lastUpdated > latest)) latest = t.lastUpdated;
    }
    return latest;
  }, [allTokens]);
  void now;

  const loadAll = useCallback(async () => {
    const authRes = await authFetch('/api/v1/auth/check');
    if (!authRes.ok) return;
    const res = await authFetch('/api/v1/tokens?cexOnly=true&take=200');
    if (res.ok) {
      const data = await res.json();
      setAllTokens(data.items ?? data);
      connectToHub();
    }
  }, []);

  const connectToHub = () => {
    startConnection();
  };

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
    const onQuote = (p: QuotePayload) => {
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
    };
    on('token.quote', onQuote);

    const unsubscribeConnection = onConnectionChange(setConnected);

    return () => { off('token.quote', onQuote); off('token.status'); unsubscribeConnection(); };
  }, []);

  const doPin = async (id: string, isPinned: boolean) => {
    await authFetch(`/api/v1/user-tokens/${id}/pin`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isPinned }),
    });
    setAllTokens(prev => prev.map(t => t.id === id ? { ...t, isPinned } : t));
  };

  const doFavorite = async (id: string, isFavorite: boolean) => {
    if (isFavorite) {
      const res = await authFetch('/api/v1/user-tokens', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tokenId: id }),
      });
      if (!res.ok) return;
    } else {
      await authFetch(`/api/v1/user-tokens/${id}`, { method: 'DELETE' });
    }
    setAllTokens(prev => prev.map(t => t.id === id ? { ...t, isFavorite } : t));
  };

  const doBlacklist = async (id: string) => {
    const res = await authFetch(`/api/v1/blacklist/${id}`, { method: 'POST' });
    if (res.ok) setAllTokens(prev => prev.filter(t => t.id !== id));
  };

  const filtered = useMemo(() => {
    let list = [...allTokens];
    if (search) {
      const q = search.toLowerCase();
      list = list.filter(t => t.symbol.toLowerCase().includes(q) || (t.name ?? '').toLowerCase().includes(q));
    }
    if (filter === 'positive') list = list.filter(t => t.spreadPct != null && t.spreadPct > 0);
    list.sort((a, b) => {
      if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1;
      return (b.spreadPct ?? -Infinity) - (a.spreadPct ?? -Infinity);
    });
    return list;
  }, [allTokens, filter, search]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <div className={`w-2.5 h-2.5 rounded-full ${connected ? 'bg-[#22c55e]' : 'bg-[#f59e0b] shimmer'}`} />
          <h2 className="text-lg font-bold text-[#f1f5f9]">Dashboard</h2>
          <span className="text-xs text-[#475569] tabular-nums">{ago(latestUpdate ?? undefined)}</span>
        </div>
      </div>

      <div className="flex gap-1.5 mb-3 flex-wrap items-center">
        {(['all', 'positive'] as const).map(v => (
          <button key={v} onClick={() => setFilter(v)}
            className={`px-2.5 py-1 rounded text-xs transition-colors ${filter === v ? 'bg-[#d97706] text-black font-medium' : 'bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8]'}`}>
            {v === 'all' ? 'All' : 'Positive spread'}
          </button>
        ))}
        <input type="text" placeholder="Search..."
          value={search} onChange={e => setSearch(e.target.value)}
          className="ml-auto px-2.5 py-1 bg-[#16171d] border border-[#2a2b36] rounded text-xs text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] w-36" />
      </div>

      <div className="flex flex-col gap-2.5">
        {filtered.map(t => (
          <TokenCard key={t.id} token={t as unknown as UserTokenDto}
            flash={flashMap.current.get(t.id) ?? null}
            ticks={ticks.get(t.id)}
            isPinned={t.isPinned}
            onPin={doPin}
            isFavorite={t.isFavorite}
            onFavorite={doFavorite}
            onBlacklist={doBlacklist}
            onClickChart={(id) => onNavigate('chart', id)}
            onClickHistory={(id) => onNavigate('history', id)} />
        ))}
        {filtered.length === 0 && (
          <p className="text-sm text-[#64748b] text-center py-8">No tokens</p>
        )}
      </div>
    </div>
  );
}