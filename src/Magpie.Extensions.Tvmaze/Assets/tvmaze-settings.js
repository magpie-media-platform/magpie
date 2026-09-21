(() => {
  const extensionContext = document.currentScript?.magpieExtensionContext;
  const settingsRuntime = window.MagpieMetadataSettings;
  if (!extensionContext?.waitUntil || !settingsRuntime) {
    throw new Error("Metadata settings runtime is unavailable");
  }
  extensionContext.waitUntil(
    settingsRuntime.register({
      tagName: "magpie-tvmaze-settings",
      title: "TVmaze Settings",
      description:
        "Configure the contact used in the TVmaze User-Agent header. No API key is required.",
      groupName: "magpie.tvmaze",
      fields: [
        {
          key: "contact",
          label: "Contact",
          type: "text",
          placeholder: "you@example.com",
          help: "Email or URL sent in the User-Agent header so TVmaze can identify the client.",
        },
      ],
    }),
  );
})();
