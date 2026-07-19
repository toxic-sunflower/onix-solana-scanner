import { useEffect, useState, useMemo, useRef, useCallback } from 'react';
import type { UserTokenDto, QuotePayload, TickPoint } from '../types';
import { authFetch } from '../lib/auth';
import connection, { startConnection } from '../lib/signalr';
import TokenCard from './TokenCard';

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  isAvailableOnCex: boolean;
}

const POPULAR_ORDER = ['SOL', 'BONK', 'WIF', 'JUP', 'PYTH', 'RAY', 'ORCA', 'JTO', 'RENDER', 'POPCAT'];

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
  const [search, setSearch] = useState('');
  const [sorts, setSorts] = useState<SortCriterion[]>([{ key: 'hasspread', dir: 'desc' }, { key: 'spread', dir: 'desc' }]);
  const [connected, setConnected] = useState(false);
  const [showAddToken, setShowAddToken] = useState(false);
  const [addSearch, setAddSearch] = useState('');
  const [addResults, setAddResults] = useState<TokenInfo[]>([]);
  const [adding, setAdding] = useState<string | null>(null);
  const [ticks, setTicks] = useState<Map<string, TickPoint[]>>(new Map());
  const flashMap = useRef<Map<string, 'up' | 'down' | null>>(new Map());
  const addInputRef = useRef<HTMLInputElement>(null);

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

  useEffect(() => {
    if (!showAddToken) return;
    const t = setTimeout(() => {
      authFetch(`/api/v1/tokens/search?q=${encodeURIComponent(addSearch)}&limit=200`)
        .then(res => res.ok ? res.json() : [])
        .then((list: TokenInfo[]) => {
          const cexOnly = list.filter(t => t.isAvailableOnCex);
          cexOnly.sort((a, b) => {
            const aPop = POPULAR_ORDER.indexOf(a.symbol.toUpperCase());
            const bPop = POPULAR_ORDER.indexOf(b.symbol.toUpperCase());
            if (aPop !== -1 && bPop !== -1) return aPop - bPop;
            if (aPop !== -1) return -1;
            if (bPop !== -1) return 1;
            return a.symbol.localeCompare(b.symbol);
          });
          setAddResults(cexOnly);
        })
        .catch(() => {});
    }, 150);
    return () => clearTimeout(t);
  }, [addSearch, showAddToken]);

  useEffect(() => {
    if (showAddToken && addInputRef.current) {
      addInputRef.current.focus();
    }
  }, [showAddToken]);

  const doAddToken = async (tokenId: string) => {
    setAdding(tokenId);
    await authFetch('/api/v1/user-tokens', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenId }),
    });
    setAdding(null);
    setAddSearch('');
    setShowAddToken(false);
    loadTokens();
  };

  const filtered = useMemo(() => {
    let result = tokens.filter(t =>
      t.symbol.toLowerCase().includes(search.toLowerCase()));

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
  }, [tokens, search, sorts]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <div className={`w-2.5 h-2.5 rounded-full ${connected ? 'bg-[#22c55e]' : 'bg-[#f59e0b] shimmer'}`} />
          <h2 className="text-lg font-bold text-[#f1f5f9]">Dashboard</h2>
        </div>
        <div className="flex gap-2">
          <input
            type="text"
            placeholder="Search token..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="px-3 py-1.5 bg-[#16171d] border border-[#2a2b36] rounded text-sm w-44 text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] transition-colors"
          />
          <button onClick={() => { setShowAddToken(v => !v); setAddSearch(''); }}
            className="px-2.5 py-1.5 bg-[#d97706] text-black font-medium rounded text-sm hover:bg-[#b45309] transition-colors whitespace-nowrap">+ Add</button>
        </div>
      </div>

      {showAddToken && (
        <div className="mb-4 bg-[#16171d] border border-[#2a2b36] rounded-lg p-3">
          <input ref={addInputRef}
            type="text" placeholder="Search tokens by name or symbol..."
            value={addSearch} onChange={e => setAddSearch(e.target.value)}
            className="w-full px-3 py-2 bg-[#111218] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] transition-colors mb-2" />
          <div className="flex flex-col gap-1 max-h-60 overflow-y-auto">
            {addResults.filter(t => !tokens.some(mt => mt.id === t.id)).map(t => (
              <div key={t.id}
                className={`flex items-center justify-between px-2.5 py-1.5 rounded cursor-pointer transition-colors ${t.isAvailableOnCex ? 'hover:bg-[#1e1f28]' : 'opacity-50'}`}
                onClick={() => t.isAvailableOnCex && doAddToken(t.id)}>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium text-[#f1f5f9]">{t.symbol}</span>
                  {t.name && <span className="text-xs text-[#64748b]">{t.name}</span>}
                  {!t.isAvailableOnCex && <span className="text-xs text-[#d97706]">no CEX</span>}
                </div>
                {t.isAvailableOnCex && (
                  <span className="text-xs text-[#f59e0b]">{adding === t.id ? '...' : '+'}</span>
                )}
              </div>
            ))}
            {addResults.length === 0 && addSearch.length >= 1 && (
              <p className="text-xs text-[#64748b] text-center py-4">No tokens found</p>
            )}
          </div>
        </div>
      )}

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

      {tokens.length === 0 && !showAddToken && (
        <div className="text-center mt-16">
          <div className="text-4xl mb-3 text-[#2a2b36]">⟐</div>
          <p className="text-[#64748b] mb-1">No tokens tracked yet</p>
          <p className="text-xs text-[#475569] mb-4">Click "+ Add" to start tracking spreads</p>
          <button onClick={() => setShowAddToken(true)}
            className="px-5 py-2 bg-[#f59e0b] text-black rounded text-sm font-semibold hover:bg-[#d97706] transition-colors">Add Tokens</button>
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

      {tokens.length > 0 && filtered.length === 0 && (
        <p className="text-center text-[#64748b] mt-8 text-sm">No tokens matching "{search}"</p>
      )}
    </div>
  );
}
