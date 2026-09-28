// Keep this URL, worker name, and version 1 messages compatible across deployments.
const ports = new Map();
const retiredSessions = new Set();
let scope;
let session;
let source;
let opened = false;
let gap = false;
let status = { state: 'connecting' };
let expiryTimer;
let retryTimer;
let retryAttempts = 0;

function send(port, data) {
    const client = ports.get(port);
    if (!client) return;
    try {
        port.postMessage({ protocol: 1, session, epoch: client.epoch, ...data });
    } catch {
        ports.delete(port);
    }
}

function broadcast(data) {
    for (const [port, client] of ports) {
        if (client.session === session && (data.type !== 'event' || client.expires > Date.now())) send(port, data);
    }
}

function setStatus(state, error) {
    status = { state, ...(error ? { error } : {}) };
    broadcast({ type: 'status', ...status });
}

function stop() {
    clearTimeout(retryTimer);
    retryTimer = undefined;
    source?.close();
    source = undefined;
}

function reconcile() {
    clearTimeout(expiryTimer);
    const now = Date.now();
    for (const [port, client] of ports) {
        if (now - client.seen > 120000) {
            ports.delete(port);
            port.close();
        }
    }
    const active = [...ports.values()].some((client) => client.session === session && client.expires > now);
    if (!active) {
        stop();
        gap = opened;
        setStatus(opened ? 'reconnecting' : 'connecting');
        return;
    }
    const expires = Math.max(
        ...[...ports.values()].filter((client) => client.session === session).map((client) => client.expires),
    );
    expiryTimer = setTimeout(reconcile, Math.min(expires - now + 1, 2147483647));
    if (source || retryTimer || retiredSessions.has(session) || status.state === 'failed') return;
    setStatus(opened ? 'reconnecting' : 'connecting');
    try {
        const connection = new EventSource(`${scope}/events/stream`, { withCredentials: true });
        source = connection;
        connection.onopen = () => {
            if (source !== connection) return;
            retryAttempts = 0;
            setStatus('connected');
            if (gap) broadcast({ type: 'event', payload: { name: 'stream.reconnected', context: {} } });
            opened = true;
            gap = false;
        };
        connection.onmessage = (event) => {
            if (source !== connection) return;
            let payload;
            try {
                payload = JSON.parse(event.data);
            } catch {
                return;
            }
            if (typeof payload?.name !== 'string' || !payload.context || typeof payload.context !== 'object') return;
            if (Object.values(payload.context).some((value) => typeof value !== 'string')) return;
            broadcast({ type: 'event', payload });
        };
        connection.onerror = () => {
            if (source !== connection) return;
            gap = true;
            if (connection.readyState === EventSource.CLOSED) {
                stop();
                if (retryAttempts >= 3) setStatus('failed');
                else {
                    setStatus('reconnecting');
                    retryTimer = setTimeout(
                        () => {
                            retryTimer = undefined;
                            reconcile();
                        },
                        1000 * 2 ** retryAttempts++,
                    );
                }
            } else {
                setStatus('reconnecting');
            }
        };
    } catch (error) {
        setStatus('failed', error instanceof Error ? error.message : undefined);
    }
}

self.onconnect = (event) => {
    const port = event.ports[0];
    port.onmessage = ({ data }) => {
        if (data?.protocol !== 1) {
            port.postMessage({ type: 'status', state: 'failed', error: 'Reload this page to update live updates.' });
            return;
        }
        if (data.type === 'detach') {
            ports.delete(port);
            port.close();
            reconcile();
            return;
        }
        if (data.type === 'invalidate') {
            if (ports.get(port)?.session !== data.session) return;
            retiredSessions.add(data.session);
            if (session === data.session) {
                stop();
                broadcast({ type: 'invalidated' });
            }
            return;
        }
        if (data.type !== 'join' && data.type !== 'ping' && data.type !== 'retry') return;
        if (typeof data.scope !== 'string' || typeof data.session !== 'string' || typeof data.epoch !== 'string')
            return;
        if (!Number.isFinite(data.expires)) return;
        if (scope && scope !== data.scope) return;
        scope = data.scope;
        if (retiredSessions.has(data.session)) return;
        if (session !== data.session) {
            if (session) retiredSessions.add(session);
            stop();
            session = data.session;
            opened = false;
            retryAttempts = 0;
            gap = false;
            status = { state: 'connecting' };
        }
        ports.set(port, { session, epoch: data.epoch, expires: data.expires, seen: Date.now() });
        if (data.type === 'retry' && status.state === 'failed') {
            stop();
            retryAttempts = 0;
            status = { state: opened ? 'reconnecting' : 'connecting' };
        }
        reconcile();
        send(port, { type: 'status', ...status });
    };
    port.start();
};

setInterval(reconcile, 10000);
