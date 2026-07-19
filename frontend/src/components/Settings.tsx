import { useEffect, useState, useCallback } from 'react';
import { authFetch, logout, logoutAll, getSessions, revokeSession, revokeOthers } from '../lib/auth';

interface TokenInfo {
  id: string;
  symbol: string;
  name?: string;
  solanaMint: string;
  decimals: number;
  isAvailableOnCex: boolean;
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
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<TokenInfo[]>([]);
  const [adding, setAdding] = useState<string | null>(null);
  const [tab, setTab] = useState<'settings' | 'tokens'>('tokens');
  const [searchFocused, setSearchFocused] = useState(false);

  useEffect(() => {
    authFetch('/api/v1/settings')
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
    getSessions().then(setSessions).catch(console.error);
    loadMyTokens();
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

  const searchTokens = useCallback(async (q: string) => {
    const res = await authFetch(`/api/v1/tokens/search?q=${encodeURIComponent(q)}&limit=200`);
    if (res.ok) {
      let list: TokenInfo[] = await res.json();
      const cexOnly = list.filter(t => t.isAvailableOnCex);
      cexOnly.sort((a, b) => {
        const aPop = POPULAR_ORDER.indexOf(a.symbol.toUpperCase());
        const bPop = POPULAR_ORDER.indexOf(b.symbol.toUpperCase());
        if (aPop !== -1 && bPop !== -1) return aPop - bPop;
        if (aPop !== -1) return -1;
        if (bPop !== -1) return 1;
        return a.symbol.localeCompare(b.symbol);
      });
      setResults(cexOnly);
    }
  }, []);

  useEffect(() => {
    const t = setTimeout(() => searchTokens(search), 150);
    return () => clearTimeout(t);
  }, [search, searchTokens]);

  useEffect(() => {
    searchTokens('');
  }, [searchTokens]);

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
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-bold text-[#f1f5f9]">My Tokens</h2>
            <span className="text-xs text-[#64748b]">{myTokens.length} tracked</span>
          </div>

          {myTokens.length === 0 && (
            <div className="text-center py-6 bg-[#16171d] rounded-lg border border-[#2a2b36]">
              <p className="text-sm text-[#64748b]">No tokens tracked yet.</p>
              <p className="text-xs text-[#475569] mt-1">Search and add tokens below.</p>
            </div>
          )}

          <div className="flex flex-col gap-1.5">
            {myTokens.map(t => (
              <div key={t.id} className="flex items-center justify-between bg-[#16171d] px-3 py-2 rounded border border-[#2a2b36]">
                <div className="flex items-center gap-2">
                  <span className="font-semibold text-sm text-[#f1f5f9]">{t.symbol}</span>
                  {t.name && <span className="text-xs text-[#64748b]">{t.name}</span>}
                </div>
                <button onClick={() => removeToken(t.id)}
                  className="px-2 py-1 text-xs rounded bg-[#2a2b36] text-[#94a3b8] hover:text-[#ef4444] hover:bg-[#3a2a2a] transition-colors">Remove</button>
              </div>
            ))}
          </div>

          <hr className="border-[#2a2b36]" />

          <div>
            <h3 className="text-base font-semibold text-[#f1f5f9] mb-2">Add Tokens</h3>
            <div className="relative">
              <input type="text"
                placeholder="Search by symbol or name... (leave empty to browse all)"
                value={search} onChange={e => setSearch(e.target.value)}
                onFocus={() => setSearchFocused(true)}
                onBlur={() => setTimeout(() => setSearchFocused(false), 200)}
                className="w-full px-3 py-2 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] placeholder-[#64748b] focus:outline-none focus:border-[#f59e0b] transition-colors" />
              {searchFocused && !search && results.length > 0 && (
                <div className="absolute -top-8 right-0 text-xs text-[#64748b]">
                  {results.length} tokens available
                </div>
              )}
            </div>
          </div>

          <div className="flex flex-col gap-1.5 max-h-96 overflow-y-auto pr-1">
            {results.map(t => {
              const tracked = myTokenIds.includes(t.id);
              return (
                <div key={t.id}
                  className="flex items-center justify-between px-3 py-2 rounded border border-[#2a2b36] bg-[#16171d] hover:border-[#3a3b48] transition-colors">
                  <div className="flex items-center gap-2 min-w-0">
                    <span className="font-semibold text-sm text-[#f1f5f9]">{t.symbol}</span>
                    {t.name && <span className="text-xs text-[#64748b] truncate">{t.name}</span>}
                    {tracked && <span className="text-xs text-[#22c55e]">✓</span>}
                  </div>
                  {!tracked && (
                    <button onClick={() => addToken(t.id)}
                      className="px-2.5 py-1 text-xs font-medium rounded bg-[#d97706] text-black hover:bg-[#b45309] transition-colors whitespace-nowrap">
                      {adding === t.id ? '...' : 'Track'}
                    </button>
                  )}
                </div>
              );
            })}
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
