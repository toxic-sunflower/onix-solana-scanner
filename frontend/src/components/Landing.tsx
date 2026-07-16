import { useEffect, useState } from 'react';

export default function Landing({ onToken }: { onToken: (token: string) => void }) {
  const [telegramId, setTelegramId] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const token = params.get('token');
    if (token) {
      window.history.replaceState({}, '', '/');
      onToken(token);
    }
  }, [onToken]);

  const loginViaTelegram = async () => {
    if (!telegramId) return;
    setLoading(true);
    try {
      const res = await fetch(`/api/v1/auth/telegram?telegramId=${telegramId}`);
      if (!res.ok) return;
      const data = await res.json();
      window.open(data.url, '_blank');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex flex-col items-center justify-center px-4">
      <div className="max-w-md text-center">
        <h1 className="text-4xl font-bold text-white mb-2">ONIX Solana Scanner</h1>
        <p className="text-gray-400 mb-6">
          Real-time spread monitoring between BingX Futures and Jupiter DEX
        </p>

        <div className="flex flex-col gap-3">
          <input
            type="number"
            placeholder="Your Telegram ID"
            value={telegramId}
            onChange={e => setTelegramId(e.target.value)}
            className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-center"
          />
          <button
            onClick={loginViaTelegram}
            disabled={loading || !telegramId}
            className="px-6 py-3 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 rounded-lg text-white font-medium"
          >
            {loading ? 'Connecting...' : 'Login via Telegram'}
          </button>
        </div>

        <p className="text-gray-500 text-sm mt-4">
          Enter your Telegram ID, then click to authenticate via bot
        </p>
      </div>
    </div>
  );
}
