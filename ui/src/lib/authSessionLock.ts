const DATABASE_NAME = 'magpie-auth-coordination';
const STORE_NAME = 'sessions';

let database: Promise<IDBDatabase> | undefined;

function openDatabase(): Promise<IDBDatabase> {
    database ??= new Promise<IDBDatabase>((resolve, reject) => {
        const request = indexedDB.open(DATABASE_NAME, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(STORE_NAME);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    }).catch((error: unknown) => {
        database = undefined;
        throw error;
    });
    return database;
}

export async function withAuthSessionLock<T>(scope: string, action: (signal: AbortSignal) => Promise<T>): Promise<T> {
    const controller = new AbortController();
    if (navigator.locks) {
        return navigator.locks.request(`magpie-auth:${scope}`, () => action(controller.signal));
    }

    // IndexedDB transactions also coordinate tabs on HTTP origins where Web Locks are unavailable.
    const db = await openDatabase();
    return new Promise<T>((resolve, reject) => {
        const transaction = db.transaction(STORE_NAME, 'readwrite');
        const store = transaction.objectStore(STORE_NAME);
        let started = false;
        let finished = false;
        let result: T;
        let failure: unknown;
        let failed = false;

        transaction.onabort = () => {
            controller.abort();
            reject(transaction.error ?? new Error('Session coordination interrupted'));
        };
        transaction.onerror = () => {
            controller.abort();
            reject(transaction.error ?? new Error('Session coordination failed'));
        };
        transaction.oncomplete = () => (failed ? reject(failure) : resolve(result));

        const keepAlive = () => {
            const request = store.get(scope);
            request.onsuccess = () => {
                if (!started) {
                    started = true;
                    void action(controller.signal).then(
                        (value) => {
                            result = value;
                            finished = true;
                        },
                        (error: unknown) => {
                            failure = error;
                            failed = true;
                            finished = true;
                        },
                    );
                }
                // Keep the transaction exclusive until the response has applied its cookies.
                if (!finished) keepAlive();
            };
        };
        keepAlive();
    });
}
