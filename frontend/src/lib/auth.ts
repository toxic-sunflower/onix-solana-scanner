let isRefreshing = false;

export async function authFetch(url: string, options?: RequestInit): Promise<Response> {
  const token = localStorage.getItem('auth_token');
  const refresh = localStorage.getItem('refresh_token');
  const headers = { ...options?.headers, 'X-Auth-Token': token ?? '' } as Record<string, string>;

  let res = await fetch(url, { ...options, headers });

  if (res.status === 401 && refresh && !isRefreshing && url !== '/api/v1/auth/refresh') {
    isRefreshing = true;
    try {
      const refreshRes = await fetch('/api/v1/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: refresh }),
      });

      if (refreshRes.ok) {
        const data = await refreshRes.json();
        localStorage.setItem('auth_token', data.token);
        localStorage.setItem('refresh_token', data.refreshToken);
        headers['X-Auth-Token'] = data.token;
        res = await fetch(url, { ...options, headers });
      } else {
        localStorage.removeItem('auth_token');
        localStorage.removeItem('refresh_token');
        window.location.reload();
      }
    } finally {
      isRefreshing = false;
    }
  }

  return res;
}

export function logout() {
  fetch('/api/v1/auth/revoke', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Auth-Token': localStorage.getItem('auth_token') ?? '' },
    body: JSON.stringify({ refreshToken: localStorage.getItem('refresh_token') }),
  }).catch(() => {});
  localStorage.removeItem('auth_token');
  localStorage.removeItem('refresh_token');
  window.location.reload();
}

export async function logoutAll() {
  const res = await authFetch('/api/v1/auth/revoke-all', { method: 'POST' });
  if (res.ok) {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('refresh_token');
    window.location.reload();
  }
}

export async function getSessions(): Promise<Session[]> {
  const refresh = localStorage.getItem('refresh_token');
  const res = await authFetch(`/api/v1/auth/sessions?currentRefreshToken=${encodeURIComponent(refresh ?? '')}`);
  if (res.ok) return res.json();
  return [];
}

export async function revokeSession(sessionId: string): Promise<boolean> {
  const refresh = localStorage.getItem('refresh_token');
  const res = await authFetch(`/api/v1/auth/sessions/${sessionId}?currentRefreshToken=${encodeURIComponent(refresh ?? '')}`, { method: 'DELETE' });
  if (res.ok) {
    const data = await res.json().catch(() => null);
    if (data?.token) localStorage.setItem('auth_token', data.token);
    return true;
  }
  return false;
}

export async function revokeOthers(): Promise<boolean> {
  const refresh = localStorage.getItem('refresh_token');
  const res = await authFetch('/api/v1/auth/revoke-others', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: refresh }),
  });
  if (res.ok) {
    const data = await res.json();
    if (data?.token) localStorage.setItem('auth_token', data.token);
    return true;
  }
  return false;
}

interface Session {
  id: string;
  deviceName: string;
  lastUsedAt: string;
  isCurrent: boolean;
}
