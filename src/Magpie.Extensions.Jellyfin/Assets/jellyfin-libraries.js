(() => {
  const extensionContext = document.currentScript?.magpieExtensionContext;
  if (!extensionContext?.fetch) {
    throw new Error("Jellyfin extension context is unavailable");
  }
  const hostKey = "host";
  const apiKeyKey = "apiKey";

  function connectionSettings(context) {
    const state = context.state;
    const settings = {};
    if (typeof state[hostKey] === "string" && state[hostKey].trim()) {
      settings[hostKey] = state[hostKey];
    }
    if (typeof state[apiKeyKey] === "string" && state[apiKeyKey].trim()) {
      settings[apiKeyKey] = state[apiKeyKey];
    }
    return settings;
  }

  function discoveryError(message, notConfigured) {
    const error = new Error("Jellyfin library discovery failed.");
    error.userMessage = message;
    if (notConfigured) {
      error.notConfigured = true;
    }
    return error;
  }

  async function fetchLibraries(serverId, settings) {
    let response;
    try {
      response = await extensionContext.fetch("/jellyfin/libraries", {
        method: "POST",
        authChallengeOnly: true,
        body: { mediaServerId: serverId, settings },
      });
    } catch (error) {
      if (
        error.status === 401 &&
        !error.isAuthenticationFailure &&
        error.details?.status === 401
      ) {
        const credentialError = discoveryError(
          "Jellyfin rejected the API key. Check the credentials and retry.",
        );
        credentialError.name = "JellyfinAuthenticationError";
        throw credentialError;
      }
      if (error.status === 401 || error.isAuthenticationFailure) {
        throw error;
      }
      throw discoveryError(
        "Failed to connect to Jellyfin. Check the host and network, then retry.",
      );
    }
    const data = await response.json();
    return Array.isArray(data) ? data : [];
  }

  function listLibraries(context) {
    const ctx = context;
    const settings = connectionSettings(ctx);
    const serverId = ctx.entityId || null;
    if (!serverId && (!settings[hostKey] || !settings[apiKeyKey])) {
      throw discoveryError(
        "Enter the Jellyfin host URL and API key to load libraries.",
        true,
      );
    }
    return fetchLibraries(serverId, settings);
  }

  const namespaceRoot = (window.magpieJellyfin = window.magpieJellyfin || {});
  namespaceRoot.listLibraries = listLibraries;
})();
