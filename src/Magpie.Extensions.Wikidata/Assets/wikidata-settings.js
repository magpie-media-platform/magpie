(() => {
  window.MagpieMetadataSettings?.register({
    tagName: "magpie-wikidata-settings",
    title: "Wikidata Settings",
    description:
      "Configure the contact used in the Wikidata User-Agent header and the default language for movie metadata lookups. No API key is required.",
    groupName: "magpie.wikidata",
    fields: [
      {
        key: "contact",
        label: "Contact",
        type: "text",
        placeholder: "you@example.com",
        help: "Email or URL sent in the User-Agent header, as required by the Wikimedia API policy.",
      },
      {
        key: "language",
        label: "Language",
        type: "text",
        placeholder: "en",
        help: "Use a Wikidata language code such as en.",
      },
    ],
  });
})();
