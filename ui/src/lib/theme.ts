import definitions from '../themes/builtins.json';
import { apiFetchResponse } from './api';

export type ThemeVariant = 'Dark' | 'Light';
export type ThemeMode = ThemeVariant | 'System';
export type ThemeSettings = {
    mode: ThemeMode;
    darkTheme: string;
    lightTheme: string;
};
export interface Theme {
    id: string;
    name: string;
    variant: ThemeVariant;
}
export interface ThemeCatalog {
    themes: Theme[];
    defaults: ThemeSettings;
}
interface BootTheme {
    id: string;
    css: string;
    color: string;
}

const builtInStyles = import.meta.glob<string>('../themes/*.css', { query: '?raw', import: 'default', eager: true });
export const builtInThemes: Theme[] = definitions.map((theme) => ({
    id: `Magpie.Api/${theme.name}/${theme.variant}`,
    name: theme.name,
    variant: theme.variant as ThemeVariant,
}));
export const initialThemeSettings: ThemeSettings = {
    mode: 'Dark',
    darkTheme: 'Magpie.Api/Tokyo Night/Dark',
    lightTheme: 'Magpie.Api/Frost/Light',
};

const storageKey = 'magpie.theme';
const bootKey = 'magpie.theme.boot';
const changeEvent = 'magpie-theme-change';
let state = {
    catalog: { themes: builtInThemes, defaults: initialThemeSettings } as ThemeCatalog,
    preference: null as ThemeSettings | null,
    error: null as string | null,
};
let requestId = 0;
let catalogLoaded = false;
let stylesheet = document.querySelector<HTMLStyleElement>('style[data-theme-stylesheet]') ?? undefined;

function isThemeSettings(value: unknown): value is ThemeSettings {
    if (!value || typeof value !== 'object') return false;
    const settings = value as Partial<ThemeSettings>;
    return (
        ['Dark', 'Light', 'System'].includes(settings.mode ?? '') &&
        typeof settings.darkTheme === 'string' &&
        typeof settings.lightTheme === 'string'
    );
}

function readPreference(): ThemeSettings | null {
    try {
        const stored: unknown = JSON.parse(localStorage.getItem(storageKey) ?? 'null');
        return isThemeSettings(stored) ? stored : null;
    } catch {
        return null;
    }
}

function notify() {
    state = { ...state };
    window.dispatchEvent(new Event(changeEvent));
}

function installStylesheet(theme?: Theme, css = '') {
    stylesheet?.remove();
    stylesheet = undefined;
    delete document.documentElement.dataset.colorScheme;
    delete document.documentElement.dataset.theme;
    if (theme) {
        stylesheet = document.createElement('style');
        stylesheet.dataset.themeStylesheet = theme.id;
        stylesheet.textContent = css;
        document.head.appendChild(stylesheet);
        document.documentElement.dataset.colorScheme = theme.variant.toLowerCase();
        document.documentElement.dataset.theme = theme.id;
    }
    const background = getComputedStyle(document.documentElement).getPropertyValue('--bg-primary').trim();
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', background);
    document
        .querySelector('meta[name="apple-mobile-web-app-status-bar-style"]')
        ?.setAttribute('content', theme?.variant === 'Light' ? 'default' : 'black-translucent');
    return background;
}

// index.html reads this record to install the theme before the first paint. Keep the two in step.
function rememberTheme(settings: ThemeSettings, variant: ThemeVariant, theme?: BootTheme) {
    try {
        const stored = JSON.parse(localStorage.getItem(bootKey) ?? '{}') as Partial<Record<ThemeVariant, BootTheme>>;
        const boot: { mode: ThemeMode } & Partial<Record<ThemeVariant, BootTheme>> = { mode: settings.mode };
        if (stored.Dark?.id === settings.darkTheme) boot.Dark = stored.Dark;
        if (stored.Light?.id === settings.lightTheme) boot.Light = stored.Light;
        boot[variant] = theme;
        localStorage.setItem(bootKey, JSON.stringify(boot));
    } catch {
        // Blocked storage must not prevent changing the current theme.
    }
}

function builtInCss(theme: Theme): string | undefined {
    const definition = definitions.find((entry) => `Magpie.Api/${entry.name}/${entry.variant}` === theme.id);
    return definition ? builtInStyles[`../themes/${definition.fileName.slice('Themes/'.length)}`] : undefined;
}

async function applyTheme() {
    const request = ++requestId;
    state.error = null;
    const settings = state.preference ?? state.catalog.defaults;
    const variant = getThemeVariant(settings);
    const id = variant === 'Light' ? settings.lightTheme : settings.darkTheme;
    const theme = state.catalog.themes.find((entry) => entry.id === id && entry.variant === variant);
    let css = theme && builtInCss(theme);
    if (theme && css === undefined) {
        notify();
        css = await apiFetchResponse(`/ui/themes/stylesheet?id=${encodeURIComponent(id)}`, { skipAuth: true })
            .then((response) => response.text())
            .catch(() => undefined);
        if (request !== requestId) return;
    }
    if (theme && css !== undefined) {
        rememberTheme(settings, variant, { id: theme.id, css, color: installStylesheet(theme, css) });
    } else {
        installStylesheet();
        rememberTheme(settings, variant);
        state.error = theme
            ? `Could not load ${theme.name}. Using Tokyo Night.`
            : 'Selected theme is unavailable. Using Tokyo Night.';
    }
    notify();
}

export function getThemeState() {
    return state;
}

export function getThemeVariant(settings: ThemeSettings): ThemeVariant {
    return settings.mode === 'System'
        ? window.matchMedia('(prefers-color-scheme: light)').matches
            ? 'Light'
            : 'Dark'
        : settings.mode;
}

export function setThemeCatalog(catalog: ThemeCatalog) {
    state.catalog = catalog;
    catalogLoaded = true;
    void applyTheme();
}

export function setThemePreference(preference: ThemeSettings | null) {
    state.preference = preference;
    try {
        if (preference === null) localStorage.removeItem(storageKey);
        else localStorage.setItem(storageKey, JSON.stringify(preference));
    } catch {
        // Blocked storage must not prevent changing the current theme.
    }
    void applyTheme();
}

export function subscribeTheme(listener: () => void) {
    window.addEventListener(changeEvent, listener);
    return () => window.removeEventListener(changeEvent, listener);
}

export function initializeTheme() {
    state.preference = readPreference();
    // The theme that index.html installed stays until the catalog gives the instance defaults.
    if (catalogLoaded || !stylesheet) void applyTheme();
    const media = window.matchMedia('(prefers-color-scheme: light)');
    const onSystemChange = () => {
        if ((state.preference ?? state.catalog.defaults).mode === 'System') void applyTheme();
    };
    const onStorage = (event: StorageEvent) => {
        if (event.key !== storageKey && event.key !== null) return;
        state.preference = readPreference();
        void applyTheme();
    };
    media.addEventListener('change', onSystemChange);
    window.addEventListener('storage', onStorage);
    return () => {
        requestId++;
        media.removeEventListener('change', onSystemChange);
        window.removeEventListener('storage', onStorage);
        installStylesheet();
    };
}
