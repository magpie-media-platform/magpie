(() => {
    const extensionContext = document.currentScript?.magpieExtensionContext;
    const settingsRuntime = window.MagpieMetadataSettings;
    if (!extensionContext?.waitUntil || !settingsRuntime) {
        throw new Error('Metadata settings runtime is unavailable');
    }

    extensionContext.waitUntil(settingsRuntime.register({
        tagName: 'magpie-tmdb-settings',
        title: 'TMDb Settings',
        description: 'Configure the TMDb API key and default language used for movie, TV, and person metadata lookups.',
        groupName: 'magpie.tmdb',
        fields: [
            { key: 'apiKey', label: 'API key', type: 'password', placeholder: '', help: 'Stored locally and sent only to Magpie.' },
            { key: 'language', label: 'Language', type: 'text', placeholder: 'en-US', help: 'Use a TMDb locale such as en-US.' },
        ],
    }));
})();
