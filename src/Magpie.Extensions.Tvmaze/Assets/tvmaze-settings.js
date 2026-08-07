(() => {
  window.MagpieMetadataSettings?.register({
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
  });
})();
