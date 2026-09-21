import { describe, expect, it } from 'vitest';

import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import { validateIntegrationSettings } from './useValidateSettings';

const schema: IntegrationSettingDefinition[] = [
    {
        key: 'host',
        type: SchemaDataType.String,
        displayName: 'Host',
        required: true,
    },
    {
        key: 'port',
        type: SchemaDataType.Number,
        displayName: 'Port',
    },
    {
        key: 'useSsl',
        type: SchemaDataType.Boolean,
        displayName: 'Use SSL',
    },
    {
        key: 'mode',
        type: SchemaDataType.String,
        displayName: 'Mode',
        options: ['Automatic', 'Manual'],
    },
    {
        key: 'password',
        type: SchemaDataType.String,
        displayName: 'Password',
        required: true,
        secret: true,
    },
];

describe('validateIntegrationSettings', () => {
    it.each([
        { key: '', type: SchemaDataType.String },
        { key: 'hidden', type: 'Unknown' },
    ])('ignores a required entry the form cannot render: $key $type', (entry) => {
        // Arrange
        const unrenderable = { ...entry, required: true } as IntegrationSettingDefinition;
        // Act
        const result = validateIntegrationSettings([unrenderable], { hidden: 'value' }, false);
        // Assert
        expect(result).toEqual({ isValid: true, issues: [] });
    });

    it.each([
        ['1,234.50', true],
        ['.5', true],
        ['12-', true],
        ['-12', true],
        ['79228162514264337593543950335', true],
        ['-79228162514264337593543950335', true],
        ['000079228162514264337593543950335.0000', true],
        ['79,228,162,514,264,337,593,543,950,335 -', true],
        ['79228162514264337593543950335.4999999999', true],
        ['79228162514264337593543950335.5', false],
        ['79228162514264337593543950336', false],
        ['-79228162514264337593543950336', false],
        ['99999999999999999999999999999999999999', false],
        ['0.123456789012345678901234567890', true],
        ['0.0000000000000000000000000000000000001', true],
        ['NaN', false],
        ['Infinity', false],
        ['1e3', false],
        ['--12', false],
        ['1.2.3', false],
    ])('checks invariant decimal shape and range: %s', (value, isValid) => {
        // Arrange
        const numberSchema: IntegrationSettingDefinition[] = [{ key: 'number', type: SchemaDataType.Number }];
        // Act
        const result = validateIntegrationSettings(numberSchema, { number: value }, false);
        // Assert
        expect(result.isValid).toBe(isValid);
        expect(result.issues).toEqual(isValid ? [] : [{ key: 'number', message: 'number must be a number.' }]);
    });

    it('reports required schema settings using their display names', () => {
        const result = validateIntegrationSettings(schema, {}, false);

        expect(result).toEqual({
            isValid: false,
            issues: [
                { key: 'host', message: 'Host is required.' },
                { key: 'password', message: 'Password is required.' },
            ],
        });
    });

    it('preserves an unchanged required secret for an existing connection', () => {
        const result = validateIntegrationSettings(schema, { HOST: 'localhost', password: '' }, true);

        expect(result).toEqual({ isValid: true, issues: [] });
    });

    it('validates number, boolean, and option settings against the schema', () => {
        const result = validateIntegrationSettings(
            schema,
            {
                host: 'localhost',
                password: 'secret',
                port: 'eighty',
                useSsl: 'yes',
                mode: 'Other',
            },
            false,
        );

        expect(result).toEqual({
            isValid: false,
            issues: [
                { key: 'port', message: 'Port must be a number.' },
                { key: 'useSsl', message: 'Use SSL must be true or false.' },
                { key: 'mode', message: 'Mode must be one of: Automatic, Manual.' },
            ],
        });
    });

    it('validates required mapping-list cells', () => {
        const mappingSchema: IntegrationSettingDefinition[] = [
            {
                key: 'pathMappings',
                type: SchemaDataType.MappingsList,
                displayName: 'Path mappings',
                rowSchema: [
                    { key: 'remotePath', type: SchemaDataType.String, displayName: 'Remote path', required: true },
                    { key: 'localPath', type: SchemaDataType.String, displayName: 'Local path', required: true },
                ],
            },
        ];

        const result = validateIntegrationSettings(
            mappingSchema,
            { pathMappings: '[{"remotePath":"/downloads","localPath":""}]' },
            false,
        );

        expect(result).toEqual({
            isValid: false,
            issues: [
                {
                    key: 'pathMappings[0].localPath',
                    message: 'Path mappings row 1: Local path is required.',
                },
            ],
        });
    });
});
