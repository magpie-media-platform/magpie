(() => {
    const endpoint = '/api/jellyfin/libraries';
    const tokenKey = 'magpie_auth_token';
    const hostKey = 'host';
    const apiKeyKey = 'apiKey';

    function connectionSettings(context) {
        const state = context.state || context.settings || {};
        const settings = {};
        if (typeof state[hostKey] === 'string' && state[hostKey].trim()) {
            settings[hostKey] = state[hostKey];
        }
        if (typeof state[apiKeyKey] === 'string' && state[apiKeyKey].trim()) {
            settings[apiKeyKey] = state[apiKeyKey];
        }
        return settings;
    }

    async function fetchLibraries(serverId, settings) {
        const headers = { 'Content-Type': 'application/json' };
        let token = null;
        try {
            token = window.localStorage.getItem(tokenKey);
        } catch {
            token = null;
        }
        if (token) {
            headers.Authorization = `Bearer ${token}`;
        }

        const response = await fetch(endpoint, {
            method: 'POST',
            credentials: 'same-origin',
            headers,
            body: JSON.stringify({ mediaServerId: serverId, settings: settings || {} }),
        });
        if (!response.ok) {
            const error = new Error('Jellyfin library discovery failed.');
            error.name = response.status === 401 ? 'JellyfinAuthenticationError' : 'JellyfinNetworkError';
            throw error;
        }
        const data = await response.json();
        return Array.isArray(data) ? data : [];
    }

    function listLibraries(context) {
        const ctx = context || {};
        const settings = connectionSettings(ctx);
        const serverId = ctx.entityId ?? ctx.serverId ?? ctx.mediaServerId ?? null;
        return fetchLibraries(serverId, settings);
    }

    const namespaceRoot = (window.magpieJellyfin = window.magpieJellyfin || {});
    namespaceRoot.listLibraries = listLibraries;
})();
