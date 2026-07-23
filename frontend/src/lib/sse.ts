import { ensureFreshToken } from './auth';

type Listener = (data: any) => void;

const listeners: Record<string, Set<Listener>> = {};
const connectionListeners = new Set<(connected: boolean) => void>();

let es: EventSource | null = null;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
let stopped = false;

function setConnected(value: boolean) {
  connectionListeners.forEach(fn => fn(value));
}

export function onConnectionChange(fn: (connected: boolean) => void) {
  connectionListeners.add(fn);
  return () => connectionListeners.delete(fn);
}

export function on(event: string, cb: Listener) {
  (listeners[event] ??= new Set()).add(cb);
}

export function off(event: string, cb?: Listener) {
  if (!cb) {
    delete listeners[event];
    return;
  }
  listeners[event]?.delete(cb);
}

function emit(event: string, data: any) {
  listeners[event]?.forEach(cb => cb(data));
}

const EVENTS = ['token.quote', 'token.status', 'token.alert'];

export async function startConnection() {
  stopped = false;
  const token = await ensureFreshToken();

  es?.close();
  es = new EventSource(`/api/v1/sse/spread?access_token=${encodeURIComponent(token)}`);

  es.onopen = () => setConnected(true);

  es.onerror = () => {
    setConnected(false);
    es?.close();
    es = null;
    if (!stopped && !reconnectTimer) {
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        startConnection();
      }, 3000);
    }
  };

  for (const event of EVENTS) {
    es.addEventListener(event, (e: MessageEvent) => {
      try {
        emit(event, JSON.parse(e.data));
      } catch {
        // ignore malformed frame
      }
    });
  }
}

export function stopConnection() {
  stopped = true;
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  es?.close();
  es = null;
  setConnected(false);
}
