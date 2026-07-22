import { useEffect, useState, useRef } from 'react';

interface TgUser {
  id: number;
  first_name?: string;
  last_name?: string;
  username?: string;
  photo_url?: string;
  auth_date: number;
  hash: string;
}

declare global {
  interface Window {
    Telegram?: {
      Login?: {
        auth: (options: { bot_id: string; request_access?: string }, callback: (user: TgUser) => void) => void;
      };
    };
  }
}

export default function Landing({ onToken }: { onToken: (token: string) => void }) {
  const [botUsername, setBotUsername] = useState('OnixSolanaScanner_Bot');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const widgetLoaded = useRef(false);

  useEffect(() => {
    fetch('/api/v1/config')
      .then(r => r.json())
      .then(c => { if (c.botUsername) setBotUsername(c.botUsername); })
      .catch(() => {});
  }, []);

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
    if (widgetLoaded.current) return;
    widgetLoaded.current = true;

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.async = true;
    script.setAttribute('data-telegram-login', botUsername);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-request-access', 'write');
    script.setAttribute('data-onauth', 'onTelegramAuth(user)');
    document.body.appendChild(script);

    (window as any).onTelegramAuth = (user: TgUser) => {
      setLoading(true);
      setError('');
      fetch('/api/v1/auth/tg-widget', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(user),
      })
        .then(r => r.json().then(d => ({ ok: r.ok, data: d })))
        .then(({ ok, data }) => {
          if (ok) {
            localStorage.setItem('auth_token', data.token);
            localStorage.setItem('refresh_token', data.refreshToken);
            onToken(data.token);
          } else {
            setError(data.error || 'Login failed');
            setLoading(false);
          }
        })
        .catch(() => {
          setError('Network error');
          setLoading(false);
        });
    };

    return () => { delete (window as any).onTelegramAuth; };
  }, [botUsername, onToken]);

  return (
    <div className="min-h-screen flex flex-col items-center justify-center px-4">
      <div className="max-w-md text-center">
        <h1 className="text-4xl font-bold text-white mb-2">ONIX Solana Scanner</h1>
        <p className="text-gray-400 mb-6">
          Real-time spread monitoring between BingX Futures and Jupiter DEX
        </p>

        {loading ? (
          <div className="text-gray-400">Logging in...</div>
        ) : (
          <div id="telegram-login-widget" className="flex justify-center" />
        )}

        {error && (
          <p className="text-red-400 text-sm mt-3">{error}</p>
        )}

        <p className="text-gray-500 text-sm mt-4">
          Log in with Telegram to access the dashboard
        </p>
      </div>
    </div>
  );
}
