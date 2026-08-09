(() => {
  class LifecycleSettings extends HTMLElement {
    connectedCallback() {
      this.textContent = "Lifecycle settings loaded.";
      this.setAttribute("data-fixture", "lifecycle-settings");
    }
  }

  class LifecyclePage extends HTMLElement {
    connectedCallback() {
      this.textContent =
        "Lifecycle page __LIFECYCLE_VERSION__ loaded for a viewer.";
      this.setAttribute("data-fixture", "lifecycle-page");
    }
  }

  if (!customElements.get("magpie-lifecycle-settings")) {
    customElements.define("magpie-lifecycle-settings", LifecycleSettings);
  }
  if (!customElements.get("magpie-lifecycle-page")) {
    customElements.define("magpie-lifecycle-page", LifecyclePage);
  }
})();
