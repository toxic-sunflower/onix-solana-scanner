import { useState } from 'react';
import Dashboard from './components/Dashboard';
import ChartPage from './components/ChartPage';
import Settings from './components/Settings';

export default function App() {
  const [page, setPage] = useState<'dashboard' | 'chart' | 'settings'>('dashboard');
  const [chartTokenId, setChartTokenId] = useState<string | null>(null);

  const navigate = (p: string, tokenId?: string) => {
    if (p === 'chart' && tokenId) {
      setChartTokenId(tokenId);
      setPage('chart');
    } else if (p === 'settings') {
      setPage('settings');
    } else {
      setPage('dashboard');
    }
  };

  return (
    <div className="min-h-screen bg-gray-950 text-gray-200">
      {page === 'dashboard' && <Dashboard onNavigate={navigate} />}
      {page === 'chart' && chartTokenId && (
        <ChartPage tokenId={chartTokenId} onBack={() => setPage('dashboard')} />
      )}
      {page === 'settings' && <Settings onBack={() => setPage('dashboard')} />}
    </div>
  );
}
