(() => {
  const extensionContext = document.currentScript?.magpieExtensionContext;
  const settingsRuntime = window.MagpieMetadataSettings;
  if (!extensionContext?.waitUntil || !settingsRuntime) {
    throw new Error("Metadata settings runtime is unavailable");
  }
  extensionContext.waitUntil(
    settingsRuntime.register({
      tagName: "magpie-openlibrary-settings",
      title: "Open Library Settings",
      description:
        "Configure the contact used in the Open Library User-Agent header for book metadata and cover lookups. No API key is required. Open Library cover provenance is mixed, so covers can come from multiple contributors and edition records.",
      groupName: "magpie.openlibrary",
      fields: [
        {
          key: "contact",
          label: "Contact",
          type: "text",
          placeholder: "you@example.com",
          help: "Email or URL sent in the User-Agent header so Open Library can identify the client.",
        },
      ],
    }),
  );
})();
