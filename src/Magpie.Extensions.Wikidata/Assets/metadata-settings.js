(() => {
  if (window.MagpieMetadataSettings) {
    return;
  }

  const adminRoleName = "admin";
  const extensionContext = document.currentScript?.magpieExtensionContext;
  if (
    !extensionContext?.loadAssetText ||
    !extensionContext.waitUntil ||
    !extensionContext.fetch
  ) {
    throw new Error("Metadata settings extension context is unavailable");
  }
  extensionContext.waitUntil(initialize(extensionContext));

  async function initialize(context) {
    const template = await loadTemplate(context);
    if (window.MagpieMetadataSettings) {
      return;
    }
    window.MagpieMetadataSettings = {
      register: (settingsConfig) => register(settingsConfig, template),
    };
  }

  async function loadTemplate(context) {
    const templateDocument = new DOMParser().parseFromString(
      await context.loadAssetText("metadata-settings.html"),
      "text/html",
    );
    const template = templateDocument.querySelector(
      "template#metadata-settings-template",
    );
    if (!template) {
      throw new Error("Metadata settings template is invalid");
    }
    return template;
  }

  function baseUrl(element) {
    return (
      element.getAttribute("data-magpie-api-base") ||
      extensionContext.apiBaseUrl
    ).replace(/\/+$/, "");
  }

  function request(element, path, options = {}) {
    return extensionContext.fetch(path, {
      ...options,
      baseUrl: baseUrl(element),
    });
  }

  function clean(value) {
    return String(value || "").trim();
  }

  function readSettings(group) {
    const settings = Object.fromEntries(
      ((group && group.settings) || []).map((setting) => [
        setting.key,
        setting.value,
      ]),
    );
    const configuredSecrets = new Set();
    for (const [key, state] of Object.entries(
      (group && group.secretSettings) || {},
    )) {
      if (state && state.isSet) {
        settings[key] = "";
        configuredSecrets.add(key);
      }
    }
    return { settings, configuredSecrets };
  }

  function isVisible(element) {
    const type = (
      element.getAttribute("data-magpie-entity-type") || ""
    ).toLowerCase();
    const entityId = (
      element.getAttribute("data-magpie-entity-id") || ""
    ).toLowerCase();
    const extensionId = (
      element.getAttribute("data-magpie-extension-id") || ""
    ).toLowerCase();
    if (type === "" && entityId === "") {
      return extensionId !== "";
    }
    return type === "extension" && entityId !== "" && entityId === extensionId;
  }

  function canEdit(element) {
    return (
      (element.getAttribute("data-magpie-role") || "").trim().toLowerCase() ===
      adminRoleName
    );
  }

  async function register(settingsConfig, template) {
    if (customElements.get(settingsConfig.tagName)) {
      return;
    }

    class MetadataSettingsElement extends HTMLElement {
      static get observedAttributes() {
        return [
          "data-magpie-api-base",
          "data-magpie-entity-id",
          "data-magpie-entity-type",
          "data-magpie-extension-id",
          "data-magpie-role",
        ];
      }

      constructor() {
        super();
        this.attachShadow({ mode: "open" });
        this.key = "";
        this.loaded = false;
        this.loading = false;
        this.saving = false;
        this.notice = "";
        this.error = "";
        this.values = {};
        this.original = {};
        this.originalKeys = new Set();
        this.configuredSecrets = new Set();
        this.secretClears = new Set();
        this.onSave = this.onSave.bind(this);
      }

      connectedCallback() {
        this.loadIfNeeded();
      }

      attributeChangedCallback() {
        if (this.isConnected) {
          this.loadIfNeeded();
        }
      }

      loadIfNeeded() {
        if (!isVisible(this)) {
          this.shadowRoot.replaceChildren();
          return;
        }

        const nextKey = [
          baseUrl(this),
          this.getAttribute("data-magpie-extension-id") || "",
          settingsConfig.groupName,
        ].join(":");
        if (this.loaded && this.key === nextKey) {
          this.render();
          return;
        }
        if (this.loading && this.key === nextKey) {
          return;
        }

        this.key = nextKey;
        this.load();
      }

      async load() {
        this.loading = true;
        this.loaded = false;
        this.error = "";
        this.notice = "";
        this.render();

        try {
          const extensionId =
            this.getAttribute("data-magpie-extension-id") || "";
          const response = await request(
            this,
            `/settings/groups/${encodeURIComponent(extensionId)}/${encodeURIComponent(settingsConfig.groupName)}`,
            { method: "GET" },
          ).catch((error) => {
            if (error.status === 404) {
              return null;
            }
            throw error;
          });

          let record = {};
          let configuredSecrets = new Set();
          if (response) {
            const settingsState = readSettings(await response.json());
            record = settingsState.settings;
            configuredSecrets = settingsState.configuredSecrets;
          }

          this.originalKeys = new Set(Object.keys(record));
          this.configuredSecrets = configuredSecrets;
          this.secretClears.clear();
          this.original = {};
          this.values = {};
          for (const field of settingsConfig.fields) {
            const value = record[field.key] || "";
            this.original[field.key] = value;
            this.values[field.key] = value;
          }
          this.loaded = true;
        } catch (error) {
          this.error =
            error instanceof Error ? error.message : "Failed to load settings";
        } finally {
          this.loading = false;
          this.render();
        }
      }

      changed() {
        return settingsConfig.fields.some((field) => {
          if (field.type === "password") {
            return (
              clean(this.values[field.key]) !== "" ||
              this.secretClears.has(field.key)
            );
          }
          return clean(this.values[field.key]) !== this.original[field.key];
        });
      }

      patch() {
        const add = {};
        const update = {};
        const remove = [];
        const saved = {};
        const savedConfiguredSecrets = new Set(this.configuredSecrets);

        for (const field of settingsConfig.fields) {
          const value = clean(this.values[field.key]);
          if (field.type === "password") {
            saved[field.key] = "";
            if (this.secretClears.has(field.key)) {
              if (value) {
                if (this.configuredSecrets.has(field.key)) {
                  update[field.key] = value;
                } else {
                  add[field.key] = value;
                }
                savedConfiguredSecrets.add(field.key);
              } else if (this.configuredSecrets.has(field.key)) {
                remove.push(field.key);
                savedConfiguredSecrets.delete(field.key);
              }
            } else if (value) {
              if (this.configuredSecrets.has(field.key)) {
                update[field.key] = value;
              } else {
                add[field.key] = value;
              }
              savedConfiguredSecrets.add(field.key);
            }
            continue;
          }

          saved[field.key] = value;
          if (this.originalKeys.has(field.key)) {
            if (!value) {
              remove.push(field.key);
            } else if (value !== this.original[field.key]) {
              update[field.key] = value;
            }
          } else if (value) {
            add[field.key] = value;
          }
        }

        const settings = {};
        if (Object.keys(add).length > 0) {
          settings.add = add;
        }
        if (Object.keys(update).length > 0) {
          settings.update = update;
        }
        if (remove.length > 0) {
          settings.remove = remove;
        }

        return { body: { settings }, saved, savedConfiguredSecrets };
      }

      async onSave(event) {
        event.preventDefault();
        if (this.saving || !canEdit(this)) {
          if (!canEdit(this)) {
            this.notice = "";
            this.error = "";
            this.render();
          }
          return;
        }

        const { body, saved, savedConfiguredSecrets } = this.patch();
        if (Object.keys(body.settings).length === 0) {
          this.notice = "No changes to save.";
          this.error = "";
          this.render();
          return;
        }

        this.saving = true;
        this.notice = "";
        this.error = "";
        this.render();

        try {
          const extensionId =
            this.getAttribute("data-magpie-extension-id") || "";
          await request(
            this,
            `/settings/groups/${encodeURIComponent(extensionId)}/${encodeURIComponent(settingsConfig.groupName)}`,
            { method: "PATCH", body },
          );

          this.values = { ...saved };
          this.original = { ...saved };
          this.configuredSecrets = savedConfiguredSecrets;
          this.secretClears.clear();
          this.originalKeys = new Set(
            settingsConfig.fields
              .filter((field) =>
                field.type === "password"
                  ? savedConfiguredSecrets.has(field.key)
                  : saved[field.key],
              )
              .map((field) => field.key),
          );
          this.notice = "Settings saved.";
        } catch (error) {
          this.error =
            error instanceof Error ? error.message : "Failed to save settings";
        } finally {
          this.saving = false;
          this.render();
        }
      }

      reportUnsavedChanges() {
        const dirty = this.loaded && this.changed();
        if (dirty === this.reportedDirty) {
          return;
        }
        this.reportedDirty = dirty;
        this.dispatchEvent(
          new CustomEvent("magpie-unsaved-changes", {
            bubbles: true,
            composed: true,
            detail: { dirty },
          }),
        );
      }

      updateState() {
        this.reportUnsavedChanges();
        const button = this.shadowRoot.querySelector('button[type="submit"]');
        if (button) {
          button.disabled = this.saving || !canEdit(this) || !this.changed();
        }
        const status = this.shadowRoot.querySelector("[data-status]");
        if (status && (this.notice || this.error)) {
          this.notice = "";
          this.error = "";
          status.textContent = "";
          status.removeAttribute("data-kind");
        }
      }

      render() {
        this.reportUnsavedChanges();
        if (!isVisible(this)) {
          this.shadowRoot.replaceChildren();
          return;
        }

        const readOnly = !canEdit(this);
        const status =
          this.error ||
          this.notice ||
          (readOnly && !this.loading
            ? "Read-only. Admin role required to save settings."
            : "");
        const kind = this.error
          ? "error"
          : this.notice
            ? "success"
            : status
              ? "info"
              : "";

        if (!this.shadowRoot.querySelector(".panel")) {
          this.shadowRoot.replaceChildren(template.content.cloneNode(true));
        }
        const panel = this.shadowRoot.querySelector(".panel");
        panel.dataset.providerSettings = settingsConfig.groupName;
        this.shadowRoot.querySelector("[data-title]").textContent =
          settingsConfig.title;
        const description = this.shadowRoot.querySelector("[data-description]");
        description.textContent = settingsConfig.description || "";
        description.hidden = !settingsConfig.description;

        const fields = this.shadowRoot.querySelector("[data-fields]");
        fields.replaceChildren();
        if (this.loading) {
          const loading = document.createElement("div");
          loading.className = "loading";
          loading.textContent = "Loading settings...";
          fields.append(loading);
        } else {
          for (const field of settingsConfig.fields) {
            const fieldElement = document.createElement("div");
            fieldElement.className = "field";

            const inputId = `${settingsConfig.tagName}-${field.key}`;
            const label = document.createElement("label");
            label.className = "field-label";
            label.htmlFor = inputId;
            label.textContent = field.label;
            fieldElement.append(label);

            const secretConfigured = this.configuredSecrets.has(field.key);
            const unlocked = this.secretClears.has(field.key);
            const masked =
              field.type === "password" && secretConfigured && !unlocked;

            const fieldInput = document.createElement("div");
            fieldInput.className = "field-input";

            const input = document.createElement("input");
            input.id = inputId;
            input.name = field.key;
            input.type = field.type || "text";
            input.value = masked ? "" : this.values[field.key] || "";
            input.placeholder = masked ? "********" : field.placeholder || "";
            input.autocomplete = "off";
            input.readOnly = masked;
            input.disabled = readOnly || this.saving;
            fieldInput.append(input);

            if (masked && !readOnly) {
              const clearButton = document.createElement("button");
              clearButton.type = "button";
              clearButton.className = "clear-secret";
              clearButton.dataset.secretKey = field.key;
              clearButton.disabled = this.saving;
              clearButton.setAttribute("aria-label", `Clear ${field.label}`);
              clearButton.textContent = "Clear";
              fieldInput.append(clearButton);
            }
            fieldElement.append(fieldInput);

            if (field.help) {
              const help = document.createElement("span");
              help.className = "field-help";
              help.textContent = field.help;
              fieldElement.append(help);
            }
            fields.append(fieldElement);
          }
        }

        const statusElement = this.shadowRoot.querySelector("[data-status]");
        statusElement.textContent = status;
        statusElement.dataset.kind = kind;
        const button = this.shadowRoot.querySelector('button[type="submit"]');
        button.textContent = this.saving ? "Saving..." : "Save Settings";
        button.disabled = this.saving || readOnly || !this.changed();

        this.shadowRoot
          .querySelector("form")
          .addEventListener("submit", this.onSave);
        for (const input of this.shadowRoot.querySelectorAll("input[name]")) {
          input.addEventListener("input", (event) => {
            this.values[event.target.name] = event.target.value;
            this.updateState();
          });
        }
        for (const button of this.shadowRoot.querySelectorAll(
          "button[data-secret-key]",
        )) {
          button.addEventListener("click", (event) => {
            const key = event.currentTarget.dataset.secretKey;
            this.values[key] = "";
            this.secretClears.add(key);
            this.notice = "";
            this.error = "";
            this.render();
            this.shadowRoot
              .getElementById(`${settingsConfig.tagName}-${key}`)
              ?.focus();
          });
        }
      }
    }

    customElements.define(settingsConfig.tagName, MetadataSettingsElement);
  }
})();
