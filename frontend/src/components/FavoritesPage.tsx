import { useEffect, useState, useMemo, useRef, useCallback } from 'react';
import type { UserTokenDto, QuotePayload, TickPoint } from '../types';
import { authFetch } from '../lib/auth';
import { on, off, onConnectionChange, startConnection } from '../lib/sse';
import TokenCard from './TokenCard';

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

export default function FavoritesPage({ onNavigate }: Props) {
  const [tokens, setTokens] = useState<UserTokenDto[]>([]);
  const [connected, setConnected] = useState(false);
  const [ticks, setTicks] = useState<Map<string, TickPoint[]>>(new Map());
  const flashMap = useRef<Map<string, 'up' | 'down' | null>>(new Map());

  const load = useCallback(async () => {
    const res = await authFetch('/api/v1/user-tokens');
    if (res.ok) setTokens(await res.json());
    startConnection();
  }, []);

  const loadTicks = useCallback((tokenId: string) => {
    authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=100`)
      .then(res => res.ok ? res.json() : [])
      .then((data: TickPoint[]) => {
        setTicks(prev => { const m = new Map(prev); m.set(tokenId, data); return m; });
      })
      .catch(() => {});
  }, []);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { tokens.forEach(t => loadTicks(t.id)); }, [tokens, loadTicks]);

  useEffect(() => {
    const onQuote = (p: QuotePayload) => {
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
    };
    on('token.quote', onQuote);
    const unsubscribeConnection = onConnectionChange(setConnected);
    return () => { off('token.quote', onQuote); unsubscribeConnection(); };
  }, []);

  const doPin = async (id: string, isPinned: boolean) => {
    await authFetch(`/api/v1/user-tokens/${id}/pin`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isPinned }),
    });
    setTokens(prev => prev.map(t => t.id === id ? { ...t, isPinned } : t));
  };

  const doUnfavorite = async (id: string) => {
    await authFetch(`/api/v1/user-tokens/${id}`, { method: 'DELETE' });
    setTokens(prev => prev.filter(t => t.id !== id));
  };

  const doBlacklist = async (id: string) => {
    const res = await authFetch(`/api/v1/blacklist/${id}`, { method: 'POST' });
    if (res.ok) setTokens(prev => prev.filter(t => t.id !== id));
  };

  const sorted = useMemo(() => {
    const list = [...tokens];
    list.sort((a, b) => {
      if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1;
      return (b.spreadPct ?? -Infinity) - (a.spreadPct ?? -Infinity);
    });
    return list;
  }, [tokens]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <div className={`w-2.5 h-2.5 rounded-full ${connected ? 'bg-[#22c55e]' : 'bg-[#f59e0b] shimmer'}`} />
          <h2 className="text-lg font-bold text-[#f1f5f9]">Favorites</h2>
        </div>
      </div>

      <div className="flex gap-1.5 mb-3">
        <button onClick={() => onNavigate('dashboard')}
          className="px-2.5 py-1 rounded text-xs bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8] transition-colors">Dashboard</button>
        <button className="px-2.5 py-1 rounded text-xs bg-[#d97706] text-black font-medium">⭐ Favorites</button>
        <button onClick={() => onNavigate('blacklist')}
          className="px-2.5 py-1 rounded text-xs bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8] transition-colors">🚫 Blacklist</button>
      </div>

      <div className="flex flex-col gap-2.5">
        {sorted.map(t => (
          <TokenCard key={t.id} token={t}
            flash={flashMap.current.get(t.id) ?? null}
            ticks={ticks.get(t.id)}
            isPinned={t.isPinned}
            onPin={doPin}
            isFavorite
            onFavorite={doUnfavorite}
            onBlacklist={doBlacklist}
            onClickChart={(id) => onNavigate('chart', id)}
            onClickHistory={(id) => onNavigate('history', id)} />
        ))}
        {sorted.length === 0 && (
          <p className="text-sm text-[#64748b] text-center py-8">No favorites yet</p>
        )}
      </div>
    </div>
  );
}
