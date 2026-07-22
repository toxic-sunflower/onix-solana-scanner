import * as signalR from '@microsoft/signalr';

function getTokenExp(token: string): number | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    return payload.exp ?? null;
  } catch {
    return null;
  }
}

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/spread', {
    accessTokenFactory: async () => {
      let token = localStorage.getItem('auth_token') ?? '';
      const exp = getTokenExp(token);
      if (exp && exp * 1000 < Date.now() + 60000) {
        const refresh = localStorage.getItem('refresh_token');
        if (refresh) {
          try {
            const res = await fetch('/api/v1/auth/refresh', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken: refresh }),
            });
            if (res.ok) {
              const data = await res.json();
              localStorage.setItem('auth_token', data.token);
              localStorage.setItem('refresh_token', data.refreshToken);
              token = data.token;
            }
          } catch {}
        }
      }
      return token;
    }
  })
  .withAutomaticReconnect()
  .build();

export async function startConnection() {
  try {
    await connection.start();
    console.log('SignalR connected');
  } catch (err) {
    console.error('SignalR connection failed', err);
  }
}

export default connection;
