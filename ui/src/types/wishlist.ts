export interface ContentFilterOption {
    id: string;
    name: string;
}

export interface QualityTierListOption {
    id: string;
    name: string;
}

export interface TagOption {
    id: string;
    name: string;
}

export interface WishlistFormValues {
    id?: string;
    name: string;
    filterId: string | null;
    qualityTierListId: string | null;
    monitored: boolean;
    tagIds: string[];
}
