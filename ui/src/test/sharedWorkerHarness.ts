import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { resolve } from 'node:path';

import { vi } from 'vitest';

interface Port {
    onmessage: ((event: { data: unknown }) => void) | null;
    postMessage: (data: unknown) => void;
    start: () => void;
    close: () => void;
}

export function installSharedWorkerHarness(eventSource: unknown) {
    const owners = new Map<string, { onconnect: (event: { ports: Port[] }) => void }>();
    const ticks: (() => void)[] = [];
    const workers: Worker[] = [];
    class Worker {
        port: Port;
        onerror: ((event: { message?: string }) => void) | null = null;
        constructor(_url: URL, options: { name: string }) {
            let owner = owners.get(options.name);
            if (!owner) {
                owner = { onconnect: () => {} };
                owners.set(options.name, owner);
                runInNewContext(readFileSync(resolve('public/magpie-events-worker.js'), 'utf8'), {
                    self: owner,
                    EventSource: eventSource,
                    Date,
                    setTimeout,
                    clearTimeout,
                    setInterval: (tick: () => void) => {
                        ticks.push(tick);
                        return 0;
                    },
                });
            }
            const page: Port = {
                onmessage: null,
                postMessage: (data) => remote.onmessage?.({ data }),
                start() {},
                close() {},
            };
            const remote: Port = {
                onmessage: null,
                postMessage: (data) => page.onmessage?.({ data }),
                start() {},
                close() {},
            };
            this.port = page;
            owner.onconnect({ ports: [remote] });
            workers.push(this);
        }
    }
    vi.stubGlobal('SharedWorker', Worker);
    localStorage.setItem('magpie_auth_token', 'test-session');
    localStorage.setItem('magpie_token_expires', new Date(Date.now() + 600000).toISOString());
    return { workers, tick: () => ticks.forEach((tick) => tick()) };
}
