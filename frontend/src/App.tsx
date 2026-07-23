import { useState, useEffect } from 'react';
import { HashRouter, Routes, Route, useNavigate, useParams, Navigate } from 'react-router-dom';
import Dashboard from './components/Dashboard';
import ChartPage from './components/ChartPage';
import HistoryPage from './components/HistoryPage';
import Settings from './components/Settings';
import Landing from './components/Landing';
import FavoritesPage from './components/FavoritesPage';
import BlacklistPage from './components/BlacklistPage';
import { logout } from './lib/auth';

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
          <div className="flex gap-1.5">
            <button onClick={() => navigate('/settings')}
              className="px-3 py-1 text-xs rounded bg-[#1e1f28] text-[#94a3b8] hover:bg-[#2a2b36] hover:text-[#f1f5f9] transition-colors">Settings</button>
            <button onClick={logout}
              className="px-3 py-1 text-xs rounded bg-[#1e1f28] text-[#94a3b8] hover:bg-[#2a2b36] hover:text-[#f1f5f9] transition-colors">Logout</button>
          </div>
        </div>
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
  const navigate = useNavigate();
  return <BlacklistPage onNavigate={sharedNavigate(navigate)} />;
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
