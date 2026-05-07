export type AssetKind = 'Script' | 'Style' | 'Link';

export type UISlotModifier = 'Before' | 'After' | 'Replace';

export interface Asset {
    id: string;

    extensionId: string;

    name: string;

    kind: AssetKind;

    url: string | null;

    relativePath: string | null;

    linkRel: string | null;

    integrity: string | null;

    priority: number;
}

export interface UIExtension {
    id: string;

    extensionId: string;

    name: string;

    slot: string;

    modifier: UISlotModifier;

    tagName: string;

    priority: number;

    attributes: Record<string, string> | null;
}

export interface UIContext {
    apiBaseUrl: string;

    userId: string | null;

    username: string | null;

    role: string | null;

    theme: 'light' | 'dark' | null;
}

export interface UIManifest {
    assets: Asset[];

    extensions: UIExtension[];
}

export type LoaderState = 'idle' | 'loading' | 'ready' | 'error';

export interface LoaderError {
    message: string;
    cause?: unknown;
}

export interface ExtensionLoaderState {
    state: LoaderState;

    error: LoaderError | null;

    manifest: UIManifest | null;
}
