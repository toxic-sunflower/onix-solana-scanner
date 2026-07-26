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

const LANGUAGES = [
  { code: 'en', label: '🇬🇧 English' },
  { code: 'ru', label: '🇷🇺 Русский' },
  { code: 'de', label: '🇩🇪 Deutsch' },
  { code: 'es', label: '🇪🇸 Español' },
  { code: 'fr', label: '🇫🇷 Français' },
];

export default function Settings({ onBack }: { onBack: () => void }) {
  const [settings, setSettings] = useState<UserSettings>({
    minimalSpreadPct: 5,
    telegramNotificationsEnabled: true,
    cooldownSeconds: 300,
    timezone: 'UTC',
  });
  const [sessions, setSessions] = useState<Session[]>([]);
  const [language, setLanguage] = useState('en');
  const [backupCodeCount, setBackupCodeCount] = useState<number | null>(null);
  const [generatedCodes, setGeneratedCodes] = useState<string[] | null>(null);
  const [deleteConfirming, setDeleteConfirming] = useState(false);

  useEffect(() => {
    authFetch('/api/v1/settings')
      .then(res => res.json())
      .then(setSettings)
      .catch(console.error);
    getSessions().then(setSessions).catch(console.error);
    authFetch('/api/v1/auth/me')
      .then(res => res.json())
      .then(d => { if (d.language) setLanguage(d.language); })
      .catch(console.error);
    authFetch('/api/v1/auth/backup-codes/count')
      .then(res => res.json())
      .then(d => setBackupCodeCount(d.count))
      .catch(console.error);
  }, []);

  const save = async () => {
    await authFetch('/api/v1/settings', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
  };

  const saveLanguage = async (lang: string) => {
    setLanguage(lang);
    await authFetch('/api/v1/auth/me', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ language: lang }),
    });
  };

  const generateBackupCodes = async () => {
    const res = await authFetch('/api/v1/auth/backup-codes/generate', { method: 'POST' });
    if (res.ok) {
      const data = await res.json();
      setGeneratedCodes(data.codes);
      setBackupCodeCount(data.codes.length);
    }
  };

  const deleteAccount = async () => {
    const res = await authFetch('/api/v1/auth/me', { method: 'DELETE' });
    if (res.ok) {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('refresh_token');
      window.location.reload();
    }
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
          <span className="text-sm text-[#94a3b8]">Language</span>
          <select value={language} onChange={e => saveLanguage(e.target.value)}
            className="px-3 py-1.5 bg-[#16171d] border border-[#2a2b36] rounded text-sm text-[#f1f5f9] focus:outline-none focus:border-[#f59e0b]">
            {LANGUAGES.map(l => <option key={l.code} value={l.code}>{l.label}</option>)}
          </select>
        </label>

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

        <hr className="border-[#2a2b36]" />

        <h3 className="text-base font-semibold text-[#f1f5f9]">Recovery codes</h3>
        <p className="text-sm text-[#64748b]">
          If you lose access to Telegram, a recovery code lets you log in without it.
          {backupCodeCount !== null && backupCodeCount > 0 && ` ${backupCodeCount} unused code(s) remaining.`}
          {backupCodeCount === 0 && ' No codes generated yet.'}
        </p>
        <button onClick={generateBackupCodes}
          className="px-3 py-1.5 bg-[#2a2b36] text-[#94a3b8] rounded text-sm hover:bg-[#3a3b48] transition-colors self-start">
          {backupCodeCount ? 'Regenerate codes (invalidates old ones)' : 'Generate recovery codes'}
        </button>
        {generatedCodes && (
          <div className="bg-[#16171d] border border-[#2a2b36] rounded p-3 flex flex-col gap-1">
            <p className="text-xs text-[#f59e0b] mb-1">Save these now — they won't be shown again:</p>
            <div className="grid grid-cols-2 gap-1 font-mono text-sm text-[#f1f5f9]">
              {generatedCodes.map(c => <span key={c}>{c}</span>)}
            </div>
          </div>
        )}

        <hr className="border-[#2a2b36]" />

        <h3 className="text-base font-semibold text-[#ef4444]">Danger zone</h3>
        {!deleteConfirming ? (
          <button onClick={() => setDeleteConfirming(true)}
            className="px-3 py-1.5 bg-[#3a2a2a] text-[#ef4444] rounded text-sm hover:bg-[#4a2a2a] transition-colors self-start">
            Delete account
          </button>
        ) : (
          <div className="flex flex-col gap-2 items-start">
            <p className="text-sm text-[#ef4444]">
              This permanently deletes your account and all data (favorites, blacklist, alert settings, sessions). This cannot be undone.
            </p>
            <div className="flex gap-2">
              <button onClick={deleteAccount}
                className="px-3 py-1.5 bg-[#ef4444] text-black font-medium rounded text-sm hover:bg-[#dc2626] transition-colors">
                Yes, delete everything
              </button>
              <button onClick={() => setDeleteConfirming(false)}
                className="px-3 py-1.5 bg-[#2a2b36] text-[#94a3b8] rounded text-sm hover:bg-[#3a3b48] transition-colors">
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}