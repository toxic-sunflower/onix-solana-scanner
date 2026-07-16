import { useEffect, useState } from 'react';

interface UserSettings {
  minimalSpreadPct: number;
  telegramNotificationsEnabled: boolean;
  cooldownSeconds: number;
  timezone: string;
}

function authHeaders(): Record<string, string> {
  const token = localStorage.getItem('auth_token');
  return token ? { 'X-Auth-Token': token, 'Content-Type': 'application/json' } : { 'Content-Type': 'application/json' };
}

export default function Settings({ onBack }: { onBack: () => void }) {
  const [settings, setSettings] = useState<UserSettings>({
    minimalSpreadPct: 5,
    telegramNotificationsEnabled: true,
    cooldownSeconds: 300,
    timezone: 'UTC',
  });

  useEffect(() => {
    fetch('/api/v1/settings', { headers: authHeaders() })
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
  }, []);

  const save = async () => {
    await fetch('/api/v1/settings', {
      method: 'PATCH',
      headers: authHeaders(),
      body: JSON.stringify(settings),
    });
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
      </div>
    </div>
  );
}
