import { describe, expect, it } from 'vitest';

import type { JobParameterDefinition } from '@/hooks/useJobs';

import {
    buildJobParameterPatch,
    defaultJobParameterValue,
    getJobParameterInputType,
    normalizeJobParameterValues,
    seedJobParameters,
    validateJobParameters,
} from './jobParameters';

describe('import job parameters', () => {
    const collisionParameters: JobParameterDefinition[] = [
        {
            name: 'collisionMode',
            description: 'Inherit when omitted.',
            type: 'String',
            required: false,
            defaultValue: null,
        },
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
        const parameters: JobParameterDefinition[] = [
            {
                name: 'recoveryInactivityMinutes',
                description: null,
                type: 'Number',
                required: false,
                defaultValue: '60',
            },
        ];
        expect(normalizeJobParameterValues(seedJobParameters(parameters))).toEqual({ recoveryInactivityMinutes: '60' });
        expect(normalizeJobParameterValues(seedJobParameters(parameters, { recoveryInactivityMinutes: '15' }))).toEqual(
            {
                recoveryInactivityMinutes: '15',
            },
        );
    });
});

describe('job parameter types', () => {
    it.each<[JobParameterDefinition['type'], string]>([
        ['String', 'text'],
        ['Number', 'number'],
        ['Date', 'date'],
    ])('renders a %s parameter as a %s input', (type, expected) => {
        expect(getJobParameterInputType({ name: 'value', type, required: false })).toBe(expected);
    });

    it.each<[JobParameterDefinition['type'], string]>([
        ['String', ''],
        ['Number', ''],
        ['Boolean', 'false'],
        ['Date', ''],
    ])('seeds a %s parameter without a declared default as %j', (type, expected) => {
        expect(defaultJobParameterValue({ name: 'value', type, required: false, defaultValue: null })).toBe(expected);
    });

    it.each<[JobParameterDefinition['type'], string, string]>([
        ['Number', 'not-a-number', 'value must be a number'],
        ['Boolean', 'yes', 'value must be true or false'],
        ['Date', '2026-13-01', 'value must be a valid date'],
    ])('rejects a %s parameter value %j', (type, value, expected) => {
        expect(validateJobParameters([{ name: 'value', type, required: false }], { value })).toEqual({
            'parameter-value': expected,
        });
    });

    it.each<[JobParameterDefinition['type'], string]>([
        ['Number', '${event.count}'],
        ['Boolean', '${event.enabled}'],
        ['Date', '${event.releaseDate}'],
    ])('accepts an event-bound %s parameter value %j', (type, value) => {
        expect(validateJobParameters([{ name: 'value', type, required: false }], { value }, true)).toEqual({});
    });

    it('rejects an event-bound Date parameter value that does not form a date', () => {
        expect(
            validateJobParameters(
                [{ name: 'value', type: 'Date', required: false }],
                { value: '${event.year}-01' },
                true,
            ),
        ).toEqual({ 'parameter-value': 'value must be a valid date' });
    });

    it('does not require a value for a required Boolean parameter', () => {
        expect(validateJobParameters([{ name: 'value', type: 'Boolean', required: true }], { value: '' })).toEqual({});
    });
});
