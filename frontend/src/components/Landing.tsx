import { useEffect, useState } from 'react';

export default function Landing({ onToken }: { onToken: (token: string) => void }) {
  const [botUsername, setBotUsername] = useState('OnixSolanaScanner_Bot');

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const token = params.get('token');
    const refresh = params.get('refresh');
    if (token) {
      if (refresh) localStorage.setItem('refresh_token', refresh);
      window.history.replaceState({}, '', '/');
      onToken(token);
    }
  }, [onToken]);

  useEffect(() => {
    fetch('/api/v1/config')
      .then(r => r.json())
      .then(c => { if (c.botUsername) setBotUsername(c.botUsername); })
      .catch(() => {});
  }, []);

  return (
    <div className="min-h-screen flex flex-col items-center justify-center px-4">
      <div className="max-w-md text-center">
        <h1 className="text-4xl font-bold text-white mb-2">ONIX Solana Scanner</h1>
        <p className="text-gray-400 mb-6">
          Real-time spread monitoring between BingX Futures and Jupiter DEX
        </p>

        <a
          href={`https://t.me/${botUsername}`}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-block px-6 py-3 bg-[#f59e0b] hover:bg-[#d97706] rounded-lg text-black font-medium transition-colors"
        >
          Login via Telegram
        </a>

        <p className="text-gray-500 text-sm mt-4">
          Open the bot, send /start, then click the link you receive
        </p>
      </div>
    </div>
  );
}
