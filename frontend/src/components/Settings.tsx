import { useEffect, useState } from 'react';
import { authFetch, logout, logoutAll, getSessions, revokeSession, revokeOthers } from '../lib/auth';

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

export default function Settings({ onBack }: { onBack: () => void }) {
  const [settings, setSettings] = useState<UserSettings>({
    minimalSpreadPct: 5,
    telegramNotificationsEnabled: true,
    cooldownSeconds: 300,
    timezone: 'UTC',
  });
  const [sessions, setSessions] = useState<Session[]>([]);

  useEffect(() => {
    authFetch('/api/v1/settings')
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
    getSessions().then(setSessions).catch(console.error);
  }, []);

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
    </div>
  );
}