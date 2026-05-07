import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

import { fetchSetupStatus, createSetupAdmin, type SetupStatus, type CreateAdminRequest } from './setupApi';

const mockFetch = vi.fn();
global.fetch = mockFetch;

describe('setupApi', () => {
    beforeEach(() => {
        vi.resetAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('fetchSetupStatus', () => {
        it('fetches setup status from the API', async () => {
            const mockStatus: SetupStatus = {
                isRequired: true,
                isComplete: false,
            };

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 200,
                json: async () => mockStatus,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const result = await fetchSetupStatus();

            expect(result).toEqual(mockStatus);
            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/system/setup/status'),
                expect.objectContaining({
                    headers: expect.any(Headers),
                }),
            );
        });

        it('handles API errors', async () => {
            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 500,
                json: async () => ({ title: 'Internal Server Error' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(fetchSetupStatus()).rejects.toMatchObject({
                status: 500,
            });
        });
    });

    describe('createSetupAdmin', () => {
        it('creates admin user via the API', async () => {
            const request: CreateAdminRequest = {
                username: 'admin',
                password: 'password123',
            };

            const mockUser = {
                id: 'user-1',
                username: 'admin',
                email: 'admin@example.com',
                role: 3,
            };

            mockFetch.mockResolvedValueOnce({
                ok: true,
                status: 201,
                json: async () => mockUser,
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            const result = await createSetupAdmin(request);

            expect(result).toEqual(mockUser);
            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/system/setup/admin'),
                expect.objectContaining({
                    method: 'POST',
                    body: JSON.stringify(request),
                }),
            );
        });

        it('handles validation errors', async () => {
            const request: CreateAdminRequest = {
                username: 'ab',
                password: 'short',
            };

            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 400,
                json: async () => ({ title: 'Validation failed' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(createSetupAdmin(request)).rejects.toMatchObject({
                status: 400,
            });
        });

        it('handles setup already complete error', async () => {
            const request: CreateAdminRequest = {
                username: 'admin',
                password: 'password123',
            };

            mockFetch.mockResolvedValueOnce({
                ok: false,
                status: 400,
                json: async () => ({ title: 'Setup already complete' }),
                headers: new Headers({ 'content-type': 'application/json' }),
            });

            await expect(createSetupAdmin(request)).rejects.toMatchObject({
                status: 400,
                message: 'Setup already complete',
            });
        });
    });
});
