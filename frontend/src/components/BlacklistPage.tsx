import { useEffect, useState, useCallback } from 'react';
import type { BlacklistedTokenDto } from '../types';
import { authFetch } from '../lib/auth';

interface Props {
  onNavigate: (page: string) => void;
}

export default function BlacklistPage({ onNavigate }: Props) {
  const [tokens, setTokens] = useState<BlacklistedTokenDto[]>([]);

  const load = useCallback(async () => {
    const res = await authFetch('/api/v1/blacklist');
    if (res.ok) setTokens(await res.json());
  }, []);

  useEffect(() => { load(); }, [load]);

  const doRestore = async (id: string) => {
    await authFetch(`/api/v1/blacklist/${id}`, { method: 'DELETE' });
    setTokens(prev => prev.filter(t => t.id !== id));
  };

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-bold text-[#f1f5f9]">Blacklist</h2>
      </div>

      <div className="flex gap-1.5 mb-3">
        <button onClick={() => onNavigate('dashboard')}
          className="px-2.5 py-1 rounded text-xs bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8] transition-colors">Dashboard</button>
        <button onClick={() => onNavigate('favorites')}
          className="px-2.5 py-1 rounded text-xs bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8] transition-colors">⭐ Favorites</button>
        <button className="px-2.5 py-1 rounded text-xs bg-[#d97706] text-black font-medium">🚫 Blacklist</button>
      </div>

      <div className="flex flex-col gap-2">
        {tokens.map(t => (
          <div key={t.id} className="bg-[#16171d] rounded-lg border border-[#2a2b36] p-3 flex items-center justify-between">
            <div className="flex items-center gap-2.5">
              <span className="font-bold text-sm text-[#f1f5f9]">{t.symbol}</span>
              {t.name && <span className="text-xs text-[#64748b]">{t.name}</span>}
            </div>
            <button onClick={() => doRestore(t.id)}
              className="text-xs px-2.5 py-1 rounded bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] hover:bg-[#2a2b36] transition-colors">
              Restore
            </button>
          </div>
        ))}
        {tokens.length === 0 && (
          <p className="text-sm text-[#64748b] text-center py-8">Blacklist is empty</p>
        )}
      </div>
    </div>
  );
}
