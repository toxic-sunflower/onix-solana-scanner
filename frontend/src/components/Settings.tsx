import { useEffect, useState, useMemo } from 'react';
import { authFetch, logout, logoutAll, getSessions, revokeSession, revokeOthers } from '../lib/auth';

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  isAvailableOnCex: boolean;
  bingxAskPrice?: number | null;
  jupiterBuyPrice?: number | null;
  spreadPct?: number | null;
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

type ListFilter = 'all' | 'tracked' | 'untracked';

export default function Settings({ onBack }: { onBack: () => void }) {
  const [settings, setSettings] = useState<UserSettings>({
    minimalSpreadPct: 5,
    telegramNotificationsEnabled: true,
    cooldownSeconds: 300,
    timezone: 'UTC',
  });
  const [sessions, setSessions] = useState<Session[]>([]);
  const [myTokenIds, setMyTokenIds] = useState<string[]>([]);
  const [allTokens, setAllTokens] = useState<TokenInfo[]>([]);
  const [adding, setAdding] = useState<string | null>(null);
  const [tab, setTab] = useState<'settings' | 'tokens'>('tokens');
  const [filter, setFilter] = useState<ListFilter>('all');
  const [page, setPage] = useState(0);
  const pageSize = 25;

  useEffect(() => {
    authFetch('/api/v1/settings')
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
    getSessions().then(setSessions).catch(console.error);
    loadMyTokens();
    loadAll();
  }, []);

  useEffect(() => { setPage(0); }, [filter]);

  const loadMyTokens = async () => {
    const res = await authFetch('/api/v1/user-tokens');
    if (res.ok) {
      const list: any[] = await res.json();
      setMyTokenIds(list.map(t => t.id));
    }
  };

  const loadAll = async () => {
    const res = await authFetch('/api/v1/tokens/search?q=&take=200');
    if (res.ok) {
      const data = await res.json();
      setAllTokens(data.items ?? data);
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

  const displayed = useMemo(() => {
    let list = [...allTokens];
    if (filter === 'tracked') list = list.filter(t => myTokenIds.includes(t.id));
    else if (filter === 'untracked') list = list.filter(t => !myTokenIds.includes(t.id));
    list.sort((a, b) => {
      const aRank = myTokenIds.includes(a.id) ? 0 : a.isAvailableOnCex ? 1 : 2;
      const bRank = myTokenIds.includes(b.id) ? 0 : b.isAvailableOnCex ? 1 : 2;
      return aRank - bRank;
    });
    return list;
  }, [allTokens, myTokenIds, filter]);

  const totalPages = Math.max(1, Math.ceil(displayed.length / pageSize));
  const safePage = Math.min(page, totalPages - 1);
  const pageItems = displayed.slice(safePage * pageSize, (safePage + 1) * pageSize);

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
        <div className="flex flex-col gap-3">
          <div className="flex gap-1.5 items-center">
            {(['all', 'tracked', 'untracked'] as const).map(v => (
              <button key={v} onClick={() => setFilter(v)}
                className={`px-3 py-1 text-xs rounded-full transition-colors ${filter === v ? 'bg-[#f59e0b] text-black font-medium' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>
                {v === 'all' ? 'All' : v === 'tracked' ? 'Tracked' : 'Untracked'}
                {v === 'all' && <span className="ml-1 text-[10px] opacity-60">({displayed.length})</span>}
              </button>
            ))}
          </div>

          <div className="flex flex-col gap-1.5">
            {pageItems.map(t => {
              const tracked = myTokenIds.includes(t.id);
              return (
                <div key={t.id}
                  className="flex items-center justify-between px-3 py-2 rounded border border-[#2a2b36] bg-[#16171d] hover:border-[#3a3b48] transition-colors">
                  <div className="flex items-center gap-2.5 min-w-0">
                    <span className="font-semibold text-sm text-[#f1f5f9]">{t.symbol}</span>
                    {t.bingxAskPrice != null && t.bingxAskPrice > 0 && (
                      <span className="text-[10px] text-[#64748b] font-mono">
                        CEX ${Number(t.bingxAskPrice).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 6 })}
                      </span>
                    )}
                    {t.jupiterBuyPrice != null && t.jupiterBuyPrice > 0 && (
                      <span className="text-[10px] text-[#64748b] font-mono">
                        DEX ${Number(t.jupiterBuyPrice).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 6 })}
                      </span>
                    )}
                    {t.spreadPct != null && (
                      <span className={`text-[10px] font-medium ${spreadColor(t.spreadPct)}`}>
                        {t.spreadPct > 0 ? '+' : ''}{t.spreadPct.toFixed(2)}%
                      </span>
                    )}
                    {!tracked && t.isAvailableOnCex && (
                      <span className="text-[9px] text-[#22c55e] font-medium">CEX</span>
                    )}
                  </div>
                  {tracked ? (
                    <button onClick={() => removeToken(t.id)}
                      className="px-2 py-1 text-xs rounded bg-[#2a2b36] text-[#94a3b8] hover:text-[#ef4444] hover:bg-[#3a2a2a] transition-colors whitespace-nowrap">Remove</button>
                  ) : t.isAvailableOnCex ? (
                    <button onClick={() => addToken(t.id)}
                      className="px-2.5 py-1 text-xs font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors whitespace-nowrap">
                      {adding === t.id ? '...' : '+Track'}
                    </button>
                  ) : null}
                </div>
              );
            })}
          </div>

          {displayed.length === 0 && (
            <p className="text-sm text-[#64748b] text-center py-6">No tokens</p>
          )}

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-3 mt-2">
              <button onClick={() => setPage(p => Math.max(0, p - 1))} disabled={safePage === 0}
                className="px-3 py-1 text-xs rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9] disabled:opacity-30 disabled:cursor-not-allowed transition-colors">‹ Prev</button>
              <span className="text-xs text-[#64748b]">{safePage + 1} / {totalPages}</span>
              <button onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))} disabled={safePage >= totalPages - 1}
                className="px-3 py-1 text-xs rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9] disabled:opacity-30 disabled:cursor-not-allowed transition-colors">Next ›</button>
            </div>
          )}
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
