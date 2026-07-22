import { useEffect, useState } from 'react';

const VERIFIER_KEY = 'tg_oauth_code_verifier';
const STATE_KEY = 'tg_oauth_state';

function base64UrlEncode(bytes: ArrayBuffer | Uint8Array): string {
  const arr = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let binary = '';
  for (const b of arr) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function randomString(length: number): string {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes).slice(0, length);
}

async function sha256(input: string): Promise<ArrayBuffer> {
  return crypto.subtle.digest('SHA-256', new TextEncoder().encode(input));
}

interface OAuthConfig {
  oauthClientId: string;
  oauthAuthorizationEndpoint: string;
  oauthRedirectUri: string;
}

export default function Landing({ onToken }: { onToken: (token: string) => void }) {
  const [config, setConfig] = useState<OAuthConfig | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    fetch('/api/v1/config')
      .then(r => r.json())
      .then(c => setConfig(c))
      .catch(() => setError('Config load failed'));
  }, []);

  // Legacy query-string token hand-off (kept for the refresh-token flow after login).
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

  // "Log In With Telegram" OAuth2 + PKCE callback: Telegram redirects back
  // here with ?code=&state=. Exchange the code for our own session via the backend.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    if (!code) return;

    const expectedState = sessionStorage.getItem(STATE_KEY);
    const codeVerifier = sessionStorage.getItem(VERIFIER_KEY);
    window.history.replaceState({}, '', window.location.pathname);
    sessionStorage.removeItem(STATE_KEY);
    sessionStorage.removeItem(VERIFIER_KEY);

    if (!codeVerifier || !state || state !== expectedState) {
      setError('Login state mismatch, please try again');
      return;
    }

    setLoading(true);
    setError('');
    fetch('/api/v1/auth/openid', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code, codeVerifier }),
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
  }, [onToken]);

  const startLogin = async () => {
    if (!config?.oauthClientId) {
      setError('Login is not configured yet');
      return;
    }

    const codeVerifier = randomString(64);
    const state = randomString(32);
    sessionStorage.setItem(VERIFIER_KEY, codeVerifier);
    sessionStorage.setItem(STATE_KEY, state);

    const codeChallenge = base64UrlEncode(await sha256(codeVerifier));

    const url = new URL(config.oauthAuthorizationEndpoint);
    url.searchParams.set('client_id', config.oauthClientId);
    url.searchParams.set('redirect_uri', config.oauthRedirectUri);
    url.searchParams.set('response_type', 'code');
    url.searchParams.set('scope', 'openid profile');
    url.searchParams.set('state', state);
    url.searchParams.set('code_challenge', codeChallenge);
    url.searchParams.set('code_challenge_method', 'S256');

    window.location.href = url.toString();
  };

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
          <button
            onClick={startLogin}
            className="bg-[#2AABEE] hover:bg-[#229ED9] text-white font-medium px-6 py-3 rounded-lg transition-colors"
          >
            Log in with Telegram
          </button>
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
