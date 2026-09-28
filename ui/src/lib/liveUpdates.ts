import { AUTH_STORAGE_EVENT, authScope, sessionGeneration, tokenExpirationStorage, tokenStorage } from './tokenStorage';
import { getCurrentUserFromToken } from './authApi';

export interface LiveEvent {
    name: string;
    context: Record<string, string>;
}

export interface LiveUpdateStatus {
    state: 'connecting' | 'connected' | 'reconnecting' | 'unsupported' | 'failed';
    error?: string;
}

let status: LiveUpdateStatus = { state: 'connecting' };
const listeners = new Set<() => void>();
let retry = () => {};

export const getLiveUpdateStatus = () => status;
export const subscribeLiveUpdateStatus = (listener: () => void) => {
    listeners.add(listener);
    return () => {
        listeners.delete(listener);
    };
};
export const retryLiveUpdates = () => retry();

function setStatus(value: LiveUpdateStatus) {
    if (status.state === value.state && status.error === value.error) return;
    status = value;
    listeners.forEach((listener) => listener());
}

export function connectLiveUpdates(userId: string, receive: (event: LiveEvent) => void): () => void {
    if (typeof SharedWorker === 'undefined') {
        setStatus({ state: 'unsupported' });
        return () => {};
    }

    const scope = authScope();
    let worker: SharedWorker | undefined;
    let session = '';
    let epoch = '';
    let lastReply = Date.now();
    let startup: ReturnType<typeof setTimeout> | undefined;
    let disposed = false;
    let hidden = document.visibilityState === 'hidden';
    let connectedBefore = false;
    let recovering = false;
    let recoveryAttempts = 0;
    let recoveryTimer: ReturnType<typeof setTimeout> | undefined;

    const currentSession = () => {
        const storedUser = getCurrentUserFromToken();
        if (!tokenStorage.exists() || (storedUser && storedUser.id !== userId)) return '';
        return `${sessionGeneration()}:${userId}`;
    };
    const post = (type: string) =>
        worker?.port.postMessage({
            protocol: 1,
            type,
            scope,
            session,
            epoch,
            expires: tokenExpirationStorage.get()?.getTime() ?? 0,
        });
    const detach = (invalidate = false) => {
        clearTimeout(startup);
        if (worker) {
            if (invalidate) post('invalidate');
            post('detach');
            worker.port.onmessage = null;
            worker.port.onmessageerror = null;
            worker.port.close();
            worker.onerror = null;
            worker = undefined;
        }
    };
    const fail = (error?: string) => {
        detach();
        setStatus({ state: 'failed', ...(error ? { error } : {}) });
    };
    const start = () => {
        if (disposed) return;
        const next = currentSession();
        if (!next) {
            detach(true);
            session = '';
            return;
        }
        detach(session !== next);
        session = next;
        recovering ||= connectedBefore;
        epoch = crypto.getRandomValues(new Uint32Array(4)).join('-');
        setStatus({ state: connectedBefore ? 'reconnecting' : 'connecting' });
        lastReply = Date.now();
        try {
            // A stable URL and name let old and new bundles join the same worker after deployment.
            const url = new URL(`${import.meta.env.BASE_URL}magpie-events-worker.js`, window.location.origin);
            const connection = new SharedWorker(url, { name: `magpie-events-v1:${scope}` });
            worker = connection;
            connection.onerror = (event) => {
                if (worker !== connection) return;
                if (connectedBefore && recoveryAttempts < 2) {
                    detach();
                    recoveryAttempts += 1;
                    recovering = true;
                    setStatus({ state: 'reconnecting' });
                    recoveryTimer = setTimeout(start, 1000);
                } else fail(event.message || undefined);
            };
            connection.port.onmessageerror = () => fail();
            connection.port.onmessage = ({ data }) => {
                if (disposed || worker !== connection || session !== currentSession()) return;
                if (data.epoch !== epoch || data.session !== session || data.protocol !== 1) return;
                lastReply = Date.now();
                clearTimeout(startup);
                if (data.type === 'status') {
                    if (!['connecting', 'connected', 'reconnecting', 'failed'].includes(data.state)) return;
                    const wasConnected = status.state === 'connected';
                    setStatus({ state: data.state, ...(typeof data.error === 'string' ? { error: data.error } : {}) });
                    if (data.state === 'connected') {
                        if (!wasConnected && recovering) receive({ name: 'stream.reconnected', context: {} });
                        connectedBefore = true;
                        recovering = false;
                        recoveryAttempts = 0;
                    }
                } else if (data.type === 'event') {
                    if ((tokenExpirationStorage.get()?.getTime() ?? 0) <= Date.now()) return;
                    receive(data.payload as LiveEvent);
                } else if (data.type === 'invalidated') {
                    detach();
                }
            };
            connection.port.start();
            post('join');
            startup = setTimeout(() => fail(), 15000);
        } catch (error) {
            fail(error instanceof Error ? error.message : undefined);
        }
    };
    const sync = () => {
        if (session !== currentSession()) start();
        else if (worker) post('ping');
    };
    const resume = () => {
        if (document.visibilityState === 'hidden') {
            hidden = true;
            return;
        }
        if (!worker || Date.now() - lastReply > 30000) start();
        else sync();
        if (hidden && session && session === currentSession()) receive({ name: 'stream.reconnected', context: {} });
        hidden = false;
    };
    const pageHide = () => {
        hidden = true;
        detach();
    };
    const heartbeat = setInterval(() => {
        if (worker && Date.now() - lastReply > 30000 && document.visibilityState === 'visible') start();
        else sync();
    }, 10000);
    retry = () => {
        if (worker) post('retry');
        else {
            recovering = connectedBefore;
            start();
        }
    };
    window.addEventListener('storage', sync);
    window.addEventListener(AUTH_STORAGE_EVENT, sync);
    window.addEventListener('pagehide', pageHide);
    window.addEventListener('pageshow', resume);
    window.addEventListener('online', resume);
    document.addEventListener('visibilitychange', resume);
    start();

    return () => {
        disposed = true;
        clearTimeout(recoveryTimer);
        detach(session !== currentSession());
        retry = () => {};
        clearInterval(heartbeat);
        window.removeEventListener('storage', sync);
        window.removeEventListener(AUTH_STORAGE_EVENT, sync);
        window.removeEventListener('pagehide', pageHide);
        window.removeEventListener('pageshow', resume);
        window.removeEventListener('online', resume);
        document.removeEventListener('visibilitychange', resume);
    };
}
