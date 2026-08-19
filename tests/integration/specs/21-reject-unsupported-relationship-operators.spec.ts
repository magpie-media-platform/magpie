import { expect, test } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, loginAdmin } from '../helpers/api';

test('keeps a supported relationship operator', async () => {
    const { api } = await loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD);
    const filter = 'Relationships has (Kind = "cast")';

    await expect(api.get(`/api/content?filter=${encodeURIComponent(filter)}`)).resolves.toBeDefined();
});

const unsupportedFilters = [
    {
        filter: 'Relationships has (Kind >= "x")',
        message: 'Operator GreaterThanOrEqual is not supported for field Kind',
    },
    {
        filter: 'Relationships has (RelatedMetadataContainerId contains "3f2a1c4e-5b6d-47a8-9c10-2d3e4f5a6b7c")',
        message: 'Operator Contains is not supported for field RelatedMetadataContainerId',
    },
];

for (const { filter, message } of unsupportedFilters) {
    test(`returns 400 for ${filter}`, async () => {
        const { api } = await loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD);

        await expect(api.get(`/api/content?filter=${encodeURIComponent(filter)}`)).rejects.toMatchObject({
            status: 400,
            body: expect.stringContaining(message),
        });
    });
}
