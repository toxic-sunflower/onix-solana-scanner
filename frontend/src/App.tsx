import { useState, useEffect } from 'react';
import { HashRouter, Routes, Route, useNavigate, useLocation, useParams, Navigate } from 'react-router-dom';
import Dashboard from './components/Dashboard';
import ChartPage from './components/ChartPage';
import HistoryPage from './components/HistoryPage';
import Settings from './components/Settings';
import Landing from './components/Landing';
import FavoritesPage from './components/FavoritesPage';
import BlacklistPage from './components/BlacklistPage';
import { logout } from './lib/auth';

const TABS = [
  { path: '/', label: 'Dashboard' },
  { path: '/favorites', label: '⭐ Favorites' },
  { path: '/blacklist', label: '🚫 Blacklist' },
] as const;

function TabNav() {
  const navigate = useNavigate();
  const location = useLocation();

  if (!TABS.some(t => t.path === location.pathname)) return null;

  return (
    <div className="max-w-4xl mx-auto px-4 py-2 flex items-center gap-1.5">
      {TABS.map(t => (
        <button key={t.path} onClick={() => navigate(t.path)}
          className={`px-2.5 py-1 rounded text-xs transition-colors ${location.pathname === t.path ? 'bg-[#d97706] text-black font-medium' : 'bg-[#1e1f28] text-[#64748b] hover:text-[#94a3b8]'}`}>
          {t.label}
        </button>
      ))}
      <button onClick={() => navigate('/settings')}
        className="ml-auto px-2.5 py-1 rounded text-xs bg-[#1e1f28] text-[#94a3b8] hover:text-[#f59e0b] transition-colors">⚙ Settings</button>
    </div>
  );
}

function AppShell({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.key === 'Backspace' || e.key === 'ArrowLeft') && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        navigate(-1);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [navigate]);

  return (
    <div className="min-h-screen bg-[#0d0e12] text-[#f1f5f9]">
      <header className="border-b border-[#2a2b36] bg-[#0d0e12] sticky top-0 z-50">
        <div className="max-w-4xl mx-auto px-4 h-10 flex items-center justify-between">
          <h1 className="text-sm font-bold text-[#f59e0b] tracking-wide">ONIX SCANNER</h1>
          <button onClick={logout}
            className="px-3 py-1 text-xs rounded bg-[#1e1f28] text-[#94a3b8] hover:bg-[#2a2b36] hover:text-[#f1f5f9] transition-colors">Logout</button>
        </div>
        <TabNav />
      </header>
      {children}
    </div>
  );
}

function sharedNavigate(navigate: ReturnType<typeof useNavigate>) {
  return (page: string, tokenId?: string) => {
    if (page === 'chart' && tokenId) navigate(`/chart/${tokenId}`);
    else if (page === 'history' && tokenId) navigate(`/history/${tokenId}`);
    else if (page === 'settings') navigate('/settings');
    else if (page === 'favorites') navigate('/favorites');
    else if (page === 'blacklist') navigate('/blacklist');
    else navigate('/');
  };
}

function DashboardRoute() {
  const navigate = useNavigate();
  return <Dashboard onNavigate={sharedNavigate(navigate)} />;
}

function FavoritesRoute() {
  const navigate = useNavigate();
  return <FavoritesPage onNavigate={sharedNavigate(navigate)} />;
}

function BlacklistRoute() {
  return <BlacklistPage />;
}

function ChartRoute() {
  const { tokenId } = useParams<{ tokenId: string }>();
  const navigate = useNavigate();
  if (!tokenId) return <Navigate to="/" />;
  return <ChartPage tokenId={tokenId} onBack={() => navigate(-1)} />;
}

function HistoryRoute() {
  const { tokenId } = useParams<{ tokenId: string }>();
  const navigate = useNavigate();
  if (!tokenId) return <Navigate to="/" />;
  return <HistoryPage tokenId={tokenId} onBack={() => navigate(-1)} />;
}

function SettingsRoute() {
  const navigate = useNavigate();
  return <Settings onBack={() => navigate(-1)} />;
}

function AuthenticatedApp() {
  return (
    <HashRouter>
      <AppShell>
        <Routes>
          <Route path="/" element={<DashboardRoute />} />
          <Route path="/favorites" element={<FavoritesRoute />} />
          <Route path="/blacklist" element={<BlacklistRoute />} />
          <Route path="/chart/:tokenId" element={<ChartRoute />} />
          <Route path="/history/:tokenId" element={<HistoryRoute />} />
          <Route path="/settings" element={<SettingsRoute />} />
        </Routes>
      </AppShell>
    </HashRouter>
  );
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('auth_token'));

  const handleToken = (t: string) => {
    localStorage.setItem('auth_token', t);
    setToken(t);
  };

  if (!token) return <Landing onToken={handleToken} />;
  return <AuthenticatedApp />;
}
