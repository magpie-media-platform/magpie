import { describe, expect, it } from 'vitest';

import { buildJobParameterPatch, normalizeJobParameterValues, seedJobParameters } from './jobParameters';

describe('import job parameters', () => {
    const collisionParameters = [
        { name: 'collisionMode', description: 'Inherit when omitted.', type: 0, required: false, defaultValue: null },
    ];

    it('omits inherited collision policy from the shared schedule and event form values', () => {
        expect(normalizeJobParameterValues(seedJobParameters(collisionParameters))).toEqual({});
    });

    it('keeps an explicit collision choice when editing a trigger', () => {
        expect(
            normalizeJobParameterValues(seedJobParameters(collisionParameters, { collisionMode: 'replace' })),
        ).toEqual({
            collisionMode: 'replace',
        });
    });

    it('removes an explicit choice when the user returns to inheritance', () => {
        expect(buildJobParameterPatch({ collisionMode: 'fail' }, { collisionMode: '' })).toEqual({
            remove: ['collisionMode'],
        });
    });

    it('seeds the one-hour recovery threshold and keeps an explicit threshold', () => {
        const parameters = [
            { name: 'recoveryInactivityMinutes', description: null, type: 1, required: false, defaultValue: '60' },
        ];
        expect(normalizeJobParameterValues(seedJobParameters(parameters))).toEqual({ recoveryInactivityMinutes: '60' });
        expect(normalizeJobParameterValues(seedJobParameters(parameters, { recoveryInactivityMinutes: '15' }))).toEqual(
            {
                recoveryInactivityMinutes: '15',
            },
        );
    });
});
