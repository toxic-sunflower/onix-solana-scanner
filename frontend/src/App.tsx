import { useState, useCallback, useEffect } from 'react';
import Dashboard from './components/Dashboard';
import ChartPage from './components/ChartPage';
import Settings from './components/Settings';
import Landing from './components/Landing';
import { logout, logoutAll } from './lib/auth';

function getTelegramUser() {
  const tg = (window as any).Telegram?.WebApp;
  if (tg?.initDataUnsafe?.user) {
    return tg.initDataUnsafe.user as { id: number; username?: string; first_name?: string };
  }
  return null;
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('auth_token'));
  const [page, setPage] = useState<'dashboard' | 'chart' | 'settings'>('dashboard');
  const [chartTokenId, setChartTokenId] = useState<string | null>(null);

  const handleToken = useCallback((t: string) => {
    localStorage.setItem('auth_token', t);
    setToken(t);
  }, []);

  useEffect(() => {
    const tgUser = getTelegramUser();
    if (tgUser && !token) {
      fetch('/api/v1/auth/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          telegramId: tgUser.id,
          username: tgUser.username ?? '',
          displayName: tgUser.first_name ?? '',
        }),
      })
        .then(res => res.json())
        .then(data => {
          localStorage.setItem('auth_token', data.token);
          localStorage.setItem('refresh_token', data.refreshToken);
          setToken(data.token);
        })
        .catch(console.error);
    }
  }, [token]);

  if (!token) return <Landing onToken={handleToken} />;

  const navigate = (p: string, tid?: string) => {
    if (p === 'chart' && tid) { setChartTokenId(tid); setPage('chart'); }
    else if (p === 'settings') setPage('settings');
    else setPage('dashboard');
  };

  return (
    <div className="min-h-screen bg-gray-950 text-gray-200">
      <div className="flex justify-end p-2 gap-2">
        <button onClick={() => navigate('settings')} className="px-2 py-1 bg-gray-800 rounded text-xs hover:bg-gray-700">Settings</button>
        <button onClick={logout} className="px-2 py-1 bg-gray-800 rounded text-xs hover:bg-gray-700">Logout</button>
      </div>
      {page === 'dashboard' && <Dashboard onNavigate={navigate} />}
      {page === 'chart' && chartTokenId && <ChartPage tokenId={chartTokenId} onBack={() => setPage('dashboard')} />}
      {page === 'settings' && <Settings onBack={() => setPage('dashboard')} />}
    </div>
  );
}
