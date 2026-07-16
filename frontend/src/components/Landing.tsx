import { useEffect, useState } from 'react';

export default function Landing({ onToken }: { onToken: (token: string) => void }) {
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
    setLoading(true);
    try {
      const tid = crypto.randomUUID();
      const res = await fetch(`/api/v1/auth/telegram?telegramId=${tid}`);
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

        <button
          onClick={loginViaTelegram}
          disabled={loading}
          className="px-6 py-3 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 rounded-lg text-white font-medium transition-colors"
        >
          {loading ? 'Connecting...' : 'Login via Telegram'}
        </button>

        <p className="text-gray-500 text-sm mt-4">
          Authenticate via Telegram bot to access the dashboard
        </p>
      </div>
    </div>
  );
}
