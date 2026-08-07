(() => {
  window.MagpieMetadataSettings?.register({
    tagName: "magpie-musicbrainz-settings",
    title: "MusicBrainz Settings",
    description:
      "Configure the contact used in the MusicBrainz User-Agent header. MusicBrainz asks that clients identify themselves so requests can be attributed. No API key is required. Run MusicBrainz work from one Magpie process per egress IP so the source-IP request limit remains effective. Cover Art Archive images are licensed per upload and remain copyrighted by their respective owners.",
    groupName: "magpie.musicbrainz",
    fields: [
      {
        key: "contact",
        label: "Contact",
        type: "text",
        placeholder: "you@example.com",
        help: "Email or URL sent in the User-Agent header so MusicBrainz can identify the client.",
      },
    ],
  });
})();
