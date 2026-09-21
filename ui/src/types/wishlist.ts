import type { ContentFilterCriteria } from './filters';

export interface ContentFilterOption {
    id: string;
    name: string;
    ownerId?: string | null;
    criteria?: ContentFilterCriteria;
}

export interface QualityTierListOption {
    id: string;
    name: string;
    mimeTypes?: string[];
}

export interface RootFolderOption {
    id: string;
    name: string;
    path: string;
    contentType: string;
}

export interface TagOption {
    id: string;
    name: string;
}
