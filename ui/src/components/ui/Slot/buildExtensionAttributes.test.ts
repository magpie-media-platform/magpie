import { describe, expect, it } from 'vitest';

import { buildExtensionAttributes } from './buildExtensionAttributes';
import type { UIExtension } from '@/extensions/types';

function makeExtension(overrides: Partial<UIExtension> = {}): UIExtension {
    return {
        id: 'ext-1',
        extensionId: 'my-ext',
        name: 'Widget',
        slot: 'dashboard.main',
        modifier: 'After',
        tagName: 'my-widget',
        priority: 100,
        attributes: null,
        ...overrides,
    };
}

describe('buildExtensionAttributes', () => {
    it('sets core magpie context attributes', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension({ extensionId: 'ext-id', name: 'My Widget' }),
            apiBaseUrl: '/api',
            userId: 'user-1',
            username: 'alice',
            role: 'Admin',
        });

        expect(result['data-magpie-api-base']).toBe('/api');
        expect(result['data-magpie-extension-id']).toBe('ext-id');
        expect(result['data-magpie-extension-name']).toBe('My Widget');
        expect(result['data-magpie-user-id']).toBe('user-1');
        expect(result['data-magpie-username']).toBe('alice');
        expect(result['data-magpie-role']).toBe('Admin');
    });

    it('omits user context attributes when null', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension(),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        expect(result['data-magpie-api-base']).toBe('/api');
        expect(result).not.toHaveProperty('data-magpie-user-id');
        expect(result).not.toHaveProperty('data-magpie-username');
        expect(result).not.toHaveProperty('data-magpie-role');
    });

    it('includes entity context when provided', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension(),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
            entityId: '42',
            entityType: 'Episode',
        });

        expect(result['data-magpie-entity-id']).toBe('42');
        expect(result['data-magpie-entity-type']).toBe('Episode');
    });

    it('omits entity context when not provided', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension(),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        expect(result).not.toHaveProperty('data-magpie-entity-id');
        expect(result).not.toHaveProperty('data-magpie-entity-type');
    });

    it('prefixes slot context entries with the magpie data namespace', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension(),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
            context: { level: 'episode', 'declares-previews': 'true', 'selection-mode': 'false' },
        });

        expect(result['data-magpie-level']).toBe('episode');
        expect(result['data-magpie-declares-previews']).toBe('true');
        expect(result['data-magpie-selection-mode']).toBe('false');
    });

    it('omits slot context entries that have no value', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension(),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
            context: { 'poster-url': undefined, 'preview-title': '' },
        });

        expect(result).not.toHaveProperty('data-magpie-poster-url');
        expect(result).not.toHaveProperty('data-magpie-preview-title');
    });

    it('core attributes override conflicting slot context entries', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension({ extensionId: 'real-id' }),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
            context: { 'api-base': 'https://evil.example.com', 'extension-id': 'spoofed-id' },
        });

        expect(result['data-magpie-api-base']).toBe('/api');
        expect(result['data-magpie-extension-id']).toBe('real-id');
    });

    it('passes through custom extension attributes', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension({ attributes: { 'data-theme': 'dark', 'tab-label': 'Reviews' } }),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        expect(result['data-theme']).toBe('dark');
        expect(result['tab-label']).toBe('Reviews');
    });

    it('core attributes override conflicting custom extension attributes', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension({
                extensionId: 'real-id',
                attributes: {
                    'data-magpie-api-base': 'https://evil.example.com',
                    'data-magpie-extension-id': 'spoofed-id',
                    'data-magpie-user-id': 'spoofed-user',
                },
            }),
            apiBaseUrl: '/api',
            userId: 'user-1',
            username: null,
            role: null,
        });

        expect(result['data-magpie-api-base']).toBe('/api');
        expect(result['data-magpie-extension-id']).toBe('real-id');
        expect(result['data-magpie-user-id']).toBe('user-1');
    });

    it('handles null extension attributes without error', () => {
        const result = buildExtensionAttributes({
            extension: makeExtension({ attributes: null }),
            apiBaseUrl: '/api',
            userId: null,
            username: null,
            role: null,
        });

        expect(result['data-magpie-api-base']).toBe('/api');
    });
});
