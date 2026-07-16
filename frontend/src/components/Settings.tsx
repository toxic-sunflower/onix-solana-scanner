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
    <div className="p-4 max-w-xl mx-auto">
      <button onClick={onBack} className="mb-4 px-3 py-1 bg-gray-800 rounded text-sm hover:bg-gray-700">← Back</button>
      <h2 className="text-xl font-bold mb-4">Settings</h2>

      <div className="flex flex-col gap-4">
        <label className="flex flex-col gap-1">
          <span className="text-sm text-gray-400">Min Spread % for signal</span>
          <input type="number" step="0.1" value={settings.minimalSpreadPct}
            onChange={e => setSettings(s => ({ ...s, minimalSpreadPct: +e.target.value }))}
            className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded" />
        </label>

        <label className="flex items-center gap-2">
          <input type="checkbox" checked={settings.telegramNotificationsEnabled}
            onChange={e => setSettings(s => ({ ...s, telegramNotificationsEnabled: e.target.checked }))} />
          <span className="text-sm">Telegram notifications</span>
        </label>

        <label className="flex flex-col gap-1">
          <span className="text-sm text-gray-400">Cooldown (seconds)</span>
          <input type="number" value={settings.cooldownSeconds}
            onChange={e => setSettings(s => ({ ...s, cooldownSeconds: +e.target.value }))}
            className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded" />
        </label>

        <button onClick={save}
          className="px-4 py-2 bg-blue-600 rounded hover:bg-blue-700 self-start">Save</button>

        <hr className="border-gray-700" />

        <h3 className="text-lg font-semibold">Active sessions</h3>
        {sessions.length === 0 && <p className="text-sm text-gray-500">No active sessions</p>}
        <div className="flex flex-col gap-2">
          {sessions.map(s => (
            <div key={s.id} className="flex items-center justify-between bg-gray-800 px-3 py-2 rounded">
              <div className="flex flex-col">
                <span className="text-sm">{s.deviceName} {s.isCurrent && <span className="text-green-400 text-xs">(current)</span>}</span>
                <span className="text-xs text-gray-500">Last used: {new Date(s.lastUsedAt).toLocaleString()}</span>
              </div>
              {!s.isCurrent && (
                <button onClick={() => handleRevokeSession(s.id)}
                  className="px-2 py-1 text-xs bg-red-900 hover:bg-red-800 rounded">
                  Logout
                </button>
              )}
            </div>
          ))}
        </div>

        {sessions.length > 0 && (
          <div className="flex gap-2 flex-wrap">
            <button onClick={handleRevokeOthers}
              className="px-4 py-2 bg-orange-800 hover:bg-orange-700 rounded text-sm">
              Logout other devices
            </button>
            <button onClick={logoutAll}
              className="px-4 py-2 bg-red-800 hover:bg-red-700 rounded text-sm">
              Logout all devices
            </button>
          </div>
        )}

        {sessions.length > 0 && (
          <hr className="border-gray-700" />
        )}

        <button onClick={logout}
          className="px-4 py-2 bg-red-900 hover:bg-red-800 rounded text-sm self-start">
          Logout this device
        </button>
      </div>
    </div>
  );
}
