import { useEffect, useState, useMemo } from 'react';
import { authFetch, logout, logoutAll, getSessions, revokeSession, revokeOthers } from '../lib/auth';

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  solanaMint: string;
  decimals: number;
  isAvailableOnCex: boolean;
  bingxAskPrice?: number | null;
  jupiterBuyPrice?: number | null;
  spreadPct?: number | null;
  status?: string | null;
  lastUpdated?: string | null;
}

interface UserSettings {
  minimalSpreadPct: number;
  telegramNotificationsEnabled: boolean;
  cooldownSeconds: number;
  timezone: string;
}

interface Session {
  id: string;
  deviceName: string;
  lastUsedAt: string;
  isCurrent: boolean;
}

const POPULAR_ORDER = ['SOL', 'BONK', 'WIF', 'JUP', 'PYTH', 'RAY', 'ORCA', 'JTO', 'RENDER', 'POPCAT'];

type CexFilter = 'all' | 'onCex' | 'noCex';
type SpreadFilter = 'any' | 'hasSpread' | 'noSpread';
type ExchangeFilter = 'both' | 'bingx' | 'jupiter';
type SortBy = 'popularity' | 'spreadDesc' | 'spreadAsc' | 'symbol';

export default function Settings({ onBack }: { onBack: () => void }) {
  const [settings, setSettings] = useState<UserSettings>({
    minimalSpreadPct: 5,
    telegramNotificationsEnabled: true,
    cooldownSeconds: 300,
    timezone: 'UTC',
  });
  const [sessions, setSessions] = useState<Session[]>([]);
  const [myTokenIds, setMyTokenIds] = useState<string[]>([]);
  const [myTokens, setMyTokens] = useState<TokenInfo[]>([]);
  const [allTokens, setAllTokens] = useState<TokenInfo[]>([]);
  const [search, setSearch] = useState('');
  const [adding, setAdding] = useState<string | null>(null);
  const [tab, setTab] = useState<'settings' | 'tokens'>('tokens');

  const [cexFilter, setCexFilter] = useState<CexFilter>('onCex');
  const [spreadFilter, setSpreadFilter] = useState<SpreadFilter>('hasSpread');
  const [exchangeFilter, setExchangeFilter] = useState<ExchangeFilter>('both');
  const [sortBy, setSortBy] = useState<SortBy>('popularity');

  useEffect(() => {
    authFetch('/api/v1/settings')
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
    getSessions().then(setSessions).catch(console.error);
    loadMyTokens();
    loadAllTokens();
  }, []);

  const loadMyTokens = async () => {
    const res = await authFetch('/api/v1/user-tokens');
    if (res.ok) {
      const list: any[] = await res.json();
      setMyTokens(list.map(t => ({
        id: t.id,
        symbol: t.symbol,
        name: t.name,
        solanaMint: t.solanaMint,
        decimals: 0,
        isAvailableOnCex: true,
      })));
      setMyTokenIds(list.map(t => t.id));
    }
  };

  const loadAllTokens = async () => {
    const res = await authFetch(`/api/v1/tokens/search?q=&limit=200`);
    if (res.ok) {
      setAllTokens(await res.json());
    }
  };

  const addToken = async (tokenId: string) => {
    setAdding(tokenId);
    await authFetch('/api/v1/user-tokens', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tokenId }),
    });
    setAdding(null);
    loadMyTokens();
  };

  const removeToken = async (tokenId: string) => {
    await authFetch(`/api/v1/user-tokens/${tokenId}`, { method: 'DELETE' });
    loadMyTokens();
  };

  const save = async () => {
    await authFetch('/api/v1/settings', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
  };

  const handleRevokeSession = async (id: string) => {
    const ok = await revokeSession(id);
    if (ok) setSessions(s => s.filter(x => x.id !== id));
  };

  const handleRevokeOthers = async () => {
    const ok = await revokeOthers();
    if (ok) setSessions(s => s.filter(x => !x.isCurrent));
  };

  const filtered = useMemo(() => {
    let list = [...allTokens];

    if (cexFilter === 'onCex') list = list.filter(t => t.isAvailableOnCex);
    else if (cexFilter === 'noCex') list = list.filter(t => !t.isAvailableOnCex);

    if (spreadFilter === 'hasSpread') list = list.filter(t => t.spreadPct != null && t.spreadPct > 0);
    else if (spreadFilter === 'noSpread') list = list.filter(t => t.spreadPct == null || t.spreadPct <= 0);

    if (exchangeFilter === 'bingx') list = list.filter(t => t.bingxAskPrice != null && t.bingxAskPrice > 0);
    else if (exchangeFilter === 'jupiter') list = list.filter(t => t.jupiterBuyPrice != null && t.jupiterBuyPrice > 0);

    if (search) {
      const q = search.toLowerCase();
      list = list.filter(t => t.symbol.toLowerCase().includes(q) || (t.name && t.name.toLowerCase().includes(q)));
    }

    list.sort((a, b) => {
      const aTracked = myTokenIds.includes(a.id) ? 1 : 0;
      const bTracked = myTokenIds.includes(b.id) ? 1 : 0;
      if (aTracked !== bTracked) return bTracked - aTracked;

      switch (sortBy) {
        case 'popularity': {
          const aPop = POPULAR_ORDER.indexOf(a.symbol.toUpperCase());
          const bPop = POPULAR_ORDER.indexOf(b.symbol.toUpperCase());
          if (aPop !== -1 && bPop !== -1) return aPop - bPop;
          if (aPop !== -1) return -1;
          if (bPop !== -1) return 1;
          return a.symbol.localeCompare(b.symbol);
        }
        case 'spreadDesc': {
          const aS = a.spreadPct ?? -1;
          const bS = b.spreadPct ?? -1;
          return bS - aS;
        }
        case 'spreadAsc': {
          const aS = a.spreadPct ?? 999;
          const bS = b.spreadPct ?? 999;
          return aS - bS;
        }
        case 'symbol':
          return a.symbol.localeCompare(b.symbol);
      }
    });

    return list;
  }, [allTokens, cexFilter, spreadFilter, exchangeFilter, sortBy, search, myTokenIds]);

  const topBySpread = useMemo(() => {
    return filtered
      .filter(t => t.spreadPct != null && t.spreadPct > 0)
      .sort((a, b) => (b.spreadPct ?? 0) - (a.spreadPct ?? 0))
      .slice(0, 10);
  }, [filtered]);

  const topPopular = useMemo(() => {
    const tracked = new Set(myTokenIds);
    const result: TokenInfo[] = [];
    for (const sym of POPULAR_ORDER) {
      const found = allTokens.find(t => t.symbol.toUpperCase() === sym);
      if (found) result.push(found);
      if (result.length >= 10) break;
    }
    result.sort((a, b) => {
      const aT = tracked.has(a.id) ? 1 : 0;
      const bT = tracked.has(b.id) ? 1 : 0;
      return bT - aT;
    });
    return result;
  }, [allTokens, myTokenIds]);

  const spreadColor = (pct: number | null | undefined) => {
    if (pct == null) return 'text-[#64748b]';
    if (pct > 5) return 'text-[#22c55e]';
    if (pct > 2) return 'text-[#84cc16]';
    if (pct > 0) return 'text-[#eab308]';
    if (pct < -2) return 'text-[#ef4444]';
    if (pct < 0) return 'text-[#f97316]';
    return 'text-[#64748b]';
  };

  return (
    <div className="p-4 max-w-2xl mx-auto">
      <button onClick={onBack}
        className="mb-4 px-3 py-1.5 bg-[#1e1f28] rounded text-sm text-[#94a3b8] hover:text-[#f59e0b] transition-colors">← Dashboard</button>

      <div className="flex gap-2 mb-5">
        <button onClick={() => setTab('tokens')}
          className={`px-4 py-1.5 rounded text-sm font-medium transition-colors ${tab === 'tokens' ? 'bg-[#f59e0b] text-black' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>Tokens</button>
        <button onClick={() => setTab('settings')}
          className={`px-4 py-1.5 rounded text-sm font-medium transition-colors ${tab === 'settings' ? 'bg-[#f59e0b] text-black' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>Settings</button>
      </div>

      {tab === 'tokens' && (
        <div className="flex flex-col gap-4">
          {myTokens.length > 0 && (
            <>
              <div className="flex items-center justify-between">
                <h2 className="text-lg font-bold text-[#f1f5f9]">My Tokens</h2>
                <span className="text-xs text-[#64748b]">{myTokens.length} tracked</span>
              </div>
              <div className="flex flex-wrap gap-1.5">
                {myTokens.map(t => (
                  <div key={t.id}
                    className="flex items-center gap-1.5 px-2.5 py-1 bg-[#16171d] border border-[#2a2b36] rounded-full text-sm text-[#f1f5f9]">
                    <span className="font-medium">{t.symbol}</span>
                    <button onClick={() => removeToken(t.id)}
                      className="text-[#64748b] hover:text-[#ef4444] transition-colors text-xs leading-none">✕</button>
                  </div>
                ))}
              </div>
              <hr className="border-[#2a2b36]" />
            </>
          )}

          <div className="flex flex-col gap-2">
            <div className="flex flex-wrap gap-1.5 items-center">
              <span className="text-xs text-[#64748b] font-medium mr-1">CEX</span>
              {(['all', 'onCex', 'noCex'] as const).map(v => (
                <button key={v} onClick={() => setCexFilter(v)}
                  className={`px-2.5 py-1 text-xs rounded-full transition-colors ${cexFilter === v ? 'bg-[#f59e0b] text-black font-medium' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>
                  {v === 'all' ? 'All' : v === 'onCex' ? 'On CEX' : 'No CEX'}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap gap-1.5 items-center">
              <span className="text-xs text-[#64748b] font-medium mr-1">Spread</span>
              {(['any', 'hasSpread', 'noSpread'] as const).map(v => (
                <button key={v} onClick={() => setSpreadFilter(v)}
                  className={`px-2.5 py-1 text-xs rounded-full transition-colors ${spreadFilter === v ? 'bg-[#f59e0b] text-black font-medium' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>
                  {v === 'any' ? 'Any' : v === 'hasSpread' ? 'Has spread' : 'No spread'}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap gap-1.5 items-center">
              <span className="text-xs text-[#64748b] font-medium mr-1">Exchange</span>
              {(['both', 'bingx', 'jupiter'] as const).map(v => (
                <button key={v} onClick={() => setExchangeFilter(v)}
                  className={`px-2.5 py-1 text-xs rounded-full transition-colors ${exchangeFilter === v ? 'bg-[#f59e0b] text-black font-medium' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>
                  {v === 'both' ? 'Both' : v.charAt(0).toUpperCase() + v.slice(1)}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap gap-1.5 items-center">
              <span className="text-xs text-[#64748b] font-medium mr-1">Sort</span>
              {(['popularity', 'spreadDesc', 'spreadAsc', 'symbol'] as const).map(v => (
                <button key={v} onClick={() => setSortBy(v)}
                  className={`px-2.5 py-1 text-xs rounded-full transition-colors ${sortBy === v ? 'bg-[#f59e0b] text-black font-medium' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>
                  {v === 'popularity' ? 'Popularity' : v === 'spreadDesc' ? 'Spread ↓' : v === 'spreadAsc' ? 'Spread ↑' : 'A-Z'}
                </button>
              ))}
            </div>
          </div>

          <input type="text" placeholder="Search by symbol or name..."
            value={search} onChange={e => setSearch(e.target.value)}
            className="w-full px-3 py-2 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] transition-colors" />

          {topBySpread.length > 0 && (
            <div>
              <h3 className="text-xs font-semibold text-[#94a3b8] uppercase tracking-wider mb-2">🔥 Top-10 by spread</h3>
              <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-thin">
                {topBySpread.map(t => {
                  const tracked = myTokenIds.includes(t.id);
                  return (
                    <div key={t.id}
                      className={`flex-shrink-0 flex flex-col items-center gap-1 px-3 py-2 rounded-lg border ${tracked ? 'border-[#22c55e]/40 bg-[#16171d]' : 'border-[#2a2b36] bg-[#1e1f28] hover:border-[#f59e0b]/50'} transition-colors min-w-[90px]`}>
                      <span className="text-sm font-bold text-[#f1f5f9]">{t.symbol}</span>
                      {t.spreadPct != null && (
                        <span className={`text-xs font-medium ${spreadColor(t.spreadPct)}`}>
                          {t.spreadPct > 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                        </span>
                      )}
                      {tracked ? (
                        <span className="text-[10px] text-[#22c55e] font-medium">✓ Tracked</span>
                      ) : (
                        <button onClick={() => addToken(t.id)}
                          className="px-2 py-0.5 text-[10px] font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors">
                          +Add
                        </button>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          <div>
            <h3 className="text-xs font-semibold text-[#94a3b8] uppercase tracking-wider mb-2">⭐ Top-10 popular</h3>
            <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-thin">
              {topPopular.map(t => {
                const tracked = myTokenIds.includes(t.id);
                return (
                  <div key={t.id}
                    className={`flex-shrink-0 flex flex-col items-center gap-1 px-3 py-2 rounded-lg border ${tracked ? 'border-[#22c55e]/40 bg-[#16171d]' : 'border-[#2a2b36] bg-[#1e1f28] hover:border-[#f59e0b]/50'} transition-colors min-w-[90px]`}>
                    <span className="text-sm font-bold text-[#f1f5f9]">{t.symbol}</span>
                    {t.spreadPct != null && (
                      <span className={`text-xs font-medium ${spreadColor(t.spreadPct)}`}>
                        {t.spreadPct > 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                      </span>
                    )}
                    {tracked ? (
                      <span className="text-[10px] text-[#22c55e] font-medium">✓</span>
                    ) : (
                      <button onClick={() => addToken(t.id)}
                        className="px-2 py-0.5 text-[10px] font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors">
                        +Add
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          <hr className="border-[#2a2b36]" />

          <div className="flex items-center justify-between">
            <h3 className="text-base font-semibold text-[#f1f5f9]">All Tokens</h3>
            <span className="text-xs text-[#64748b]">{filtered.length} tokens</span>
          </div>

          <div className="flex flex-col gap-1.5 max-h-96 overflow-y-auto pr-1">
            {filtered.map(t => {
              const tracked = myTokenIds.includes(t.id);
              return (
                <div key={t.id}
                  className="flex items-center justify-between px-3 py-2 rounded border border-[#2a2b36] bg-[#16171d] hover:border-[#3a3b48] transition-colors">
                  <div className="flex items-center gap-2.5 min-w-0">
                    <span className="font-semibold text-sm text-[#f1f5f9]">{t.symbol}</span>
                    {t.name && <span className="text-xs text-[#64748b] truncate hidden sm:inline">{t.name}</span>}
                    {t.spreadPct != null && (
                      <span className={`text-xs font-medium ${spreadColor(t.spreadPct)}`}>
                        {t.spreadPct > 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                      </span>
                    )}
                    {t.isAvailableOnCex && (
                      <span className="text-[10px] text-[#22c55e] font-medium">CEX</span>
                    )}
                  </div>
                  {tracked ? (
                    <span className="text-xs text-[#22c55e] font-medium">✓ Tracked</span>
                  ) : (
                    <button onClick={() => addToken(t.id)}
                      className="px-2.5 py-1 text-xs font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors whitespace-nowrap">
                      {adding === t.id ? '...' : '+ Track'}
                    </button>
                  )}
                </div>
              );
            })}
            {filtered.length === 0 && (
              <p className="text-sm text-[#64748b] text-center py-6">No tokens match the filters.</p>
            )}
          </div>
        </div>
      )}

      {tab === 'settings' && (
        <div className="flex flex-col gap-4">
          <h2 className="text-lg font-bold text-[#f1f5f9]">Settings</h2>

          <label className="flex flex-col gap-1.5">
            <span className="text-sm text-[#94a3b8]">Min Spread % for signal</span>
            <input type="number" step="0.1" value={settings.minimalSpreadPct}
              onChange={e => setSettings(s => ({ ...s, minimalSpreadPct: +e.target.value }))}
              className="px-3 py-1.5 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] focus:outline-none focus:border-[#f59e0b]" />
          </label>

          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={settings.telegramNotificationsEnabled}
              onChange={e => setSettings(s => ({ ...s, telegramNotificationsEnabled: e.target.checked }))}
              className="accent-[#f59e0b]" />
            <span className="text-sm text-[#f1f5f9]">Telegram notifications</span>
          </label>

          <label className="flex flex-col gap-1.5">
            <span className="text-sm text-[#94a3b8]">Cooldown (seconds)</span>
            <input type="number" value={settings.cooldownSeconds}
              onChange={e => setSettings(s => ({ ...s, cooldownSeconds: +e.target.value }))}
              className="px-3 py-1.5 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] focus:outline-none focus:border-[#f59e0b]" />
          </label>

          <button onClick={save}
            className="px-4 py-2 bg-[#d97706] text-black font-medium rounded text-sm hover:bg-[#b45309] transition-colors self-start">Save</button>

          <hr className="border-[#2a2b36]" />

          <h3 className="text-base font-semibold text-[#f1f5f9]">Active sessions</h3>
          {sessions.length === 0 && <p className="text-sm text-[#64748b]">No active sessions</p>}
          <div className="flex flex-col gap-1.5">
            {sessions.map(s => (
              <div key={s.id} className="flex items-center justify-between bg-[#16171d] px-3 py-2 rounded border border-[#2a2b36]">
                <div className="flex flex-col">
                  <span className="text-sm text-[#f1f5f9]">{s.deviceName} {s.isCurrent && <span className="text-[#22c55e] text-xs">(current)</span>}</span>
                  <span className="text-xs text-[#64748b]">Last used: {new Date(s.lastUsedAt).toLocaleString()}</span>
                </div>
                {!s.isCurrent && (
                  <button onClick={() => handleRevokeSession(s.id)}
                    className="px-2 py-1 text-xs rounded bg-[#2a2b36] text-[#94a3b8] hover:text-[#ef4444] transition-colors">Logout</button>
                )}
              </div>
            ))}
          </div>

          {sessions.length > 0 && (
            <div className="flex gap-2 flex-wrap">
              <button onClick={handleRevokeOthers}
                className="px-3 py-1.5 bg-[#2a2b36] text-[#94a3b8] rounded text-sm hover:bg-[#3a3b48] transition-colors">Logout other devices</button>
              <button onClick={logoutAll}
                className="px-3 py-1.5 bg-[#3a2a2a] text-[#ef4444] rounded text-sm hover:bg-[#4a2a2a] transition-colors">Logout all devices</button>
            </div>
          )}

          <button onClick={logout}
            className="px-3 py-1.5 bg-[#2a2b36] text-[#94a3b8] rounded text-sm hover:bg-[#3a3b48] transition-colors self-start">Logout this device</button>
        </div>
      )}
    </div>
  );
}
