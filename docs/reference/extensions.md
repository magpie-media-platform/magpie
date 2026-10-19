# Extension contracts

[Documentation](../README.md) / [Reference](README.md)

This page lists the contracts an [extension](../development/extensions.md) can implement or consume, and the values that connect them. To choose a component by purpose, see the [component index](../development/components/README.md). For calling host services, see [Built-in services](../development/using-built-in-services.md).

An SDK interface can be replaced only if it appears under [replacement contracts](#replacement-contracts). `ISettingsService` and the [job runtime](../development/architecture.md#requests-jobs-and-doctor) (`IJobRuntime`), for example, cannot.

**On this page**

- [Entry point](#entry-point)
- [Framework and ABI compatibility](#framework-and-abi-compatibility)
- [Media and metadata implementation contracts](#media-and-metadata-implementation-contracts)
- [Integration implementation contracts](#integration-implementation-contracts)
- [Optional capabilities](#optional-capabilities)
- [Host and browser implementation contracts](#host-and-browser-implementation-contracts)
- [Settings and lifecycle](#settings-and-lifecycle)
- [Replacement contracts](#replacement-contracts)
- [Supported service consumption](#supported-service-consumption)

## Entry point

[IExtension](../../src/Magpie.Sdk/Extensions/IExtension.cs) is in `Magpie.Sdk.Extensions`. The entry assembly must contain exactly one public, concrete implementation with a public parameterless constructor.

| Member | Type or signature | Meaning |
| --- | --- | --- |
| `Id` | `Guid` | Stable identity that connects the loaded extension to its installed record. |
| `Name` | `string` | Extension name. |
| `Version` | `Version` | The default is the major, minor, and build components of the entry assembly version. |
| `TargetABI` | Nullable `string` | Optional restriction on the host ABI. The default is null. |
| `RegisterServices` | `void RegisterServices(IServiceCollection services)` | Registers components before the host builds its final service provider. |

The loader creates the entry point directly, without dependency injection. Constructor injection works only in registered components, so put operational work in a component or a [lifecycle hook](../development/components/lifecycle.md).

## Framework and ABI compatibility

`Magpie.Sdk` targets `netstandard2.1` and `net10.0`. The `netstandard2.1` target does not contain the `Api` and `Auth` contracts. A component that uses these contracts must target `net10.0`.

The host ABI must have the same major version as the `Magpie.Sdk` the extension was compiled against, and must not be older than it. `TargetABI` adds a restriction and cannot remove these rules.

| Example `TargetABI` | Additional requirement |
| --- | --- |
| Null or omitted | None |
| `"1.4.0"` | Host ABI is exactly 1.4.0 |
| `"[1.4.0,1.6.0)"` | Host ABI is 1.4.0 or later and earlier than 1.6.0 |

The `Magpie.Sdk` dependency range in the package and the `TargetABI` value are separate checks. See [Publication and compatibility](../development/publish-extensions.md#declaring-compatibility).

## Media and metadata implementation contracts

These contracts are in `Magpie.Sdk.Content`, except the release comparer, which is in `Magpie.Sdk.Releases`. The linked source files have the full signatures.

| Contract | Principal members and output | Guide |
| --- | --- | --- |
| [IContentTypeDefinitionProvider](../../src/Magpie.Sdk/Content/IContentTypeDefinitionProvider.cs) | Content type definition and file-path parsing | [Media types](../development/components/media-types.md) |
| [ISubjectKindDefinitionProvider](../../src/Magpie.Sdk/Content/ISubjectKindDefinitionProvider.cs) | `GetSubjectKindDefinitions` returns subject kinds, fields, and roles | [Subject types](../development/components/subject-types.md) |
| [IContentHierarchyMetadataProvider](../../src/Magpie.Sdk/Content/IContentHierarchyMetadataProvider.cs) | Gets a [source contribution](../development/components/metadata-providers.md) and the child items of a content hierarchy | [Metadata providers](../development/components/metadata-providers.md) |
| [ISubjectMetadataProvider](../../src/Magpie.Sdk/Content/ISubjectMetadataProvider.cs) | Gets a source contribution for a supported subject kind | [Metadata providers](../development/components/metadata-providers.md) |
| [IContentHierarchyMetadataSearcher](../../src/Magpie.Sdk/Content/IContentHierarchyMetadataSearcher.cs) | A text search returns external content candidates | [Metadata search](../development/components/metadata-search.md) |
| [IContentHierarchyMetadataRelationshipProvider](../../src/Magpie.Sdk/Content/IContentHierarchyMetadataRelationshipProvider.cs) | `GetRelationshipsAsync` returns related-subject definitions for a contribution | [Relationships](../development/components/metadata-relationships.md) |
| [IContentFactProvider](../../src/Magpie.Sdk/Content/IContentFactProvider.cs) | `CanProvideFacts` and `GetFactsAsync` add groups of facts for release search | [Content facts](../development/components/content-facts.md) |
| [IReleaseComparer](../../src/Magpie.Sdk/Releases/IReleaseComparer.cs) | `CanCompare` and `Compare` order eligible releases that have the same rank | [Release comparers](../development/components/release-comparers.md) |

These identifiers have different meanings:

| Value | Meaning |
| --- | --- |
| Content-type slug | Identifies a [collection structure](../using/collection.md#content-types-and-hierarchy). Use the same lowercase type and level slugs in definitions, providers, and results. |
| Subject-kind slug | Identifies the kind of a [related entity](../using/collection.md#subjects-and-tags). A relationship role describes how the entity takes part, not its kind. |
| `ProviderType` | Stable identity of a provider. Source contributions and configured integrations refer to it. |
| External-ID source and value | Identifies the upstream record. A display title does not identify the record. |
| `InitialSubjectContribution` | Metadata that Magpie stores when it creates a related subject. Its title and external IDs must contain the values to store. |

[MetadataContribution](../../src/Magpie.Models/Data/MetadataContribution.cs), [SubjectRelationshipDefinition](../../src/Magpie.Models/SubjectRelationshipDefinition.cs), and [MetadataSearchCandidate](../../src/Magpie.Models/MetadataSearch/MetadataSearchCandidate.cs) contain the exact model fields.

## Integration implementation contracts

These contracts are in `Magpie.Sdk.Integrations`. The media information extractor is in `Magpie.Sdk.Media`.

| Contract | Selection and responsibility | Guide |
| --- | --- | --- |
| [IIndexerIntegration](../../src/Magpie.Sdk/Integrations/IIndexerIntegration.cs) | A configured indexer selects a provider type for tests, search, and payload access. | [Indexers](../development/components/indexers.md) |
| [IIndexerFeedProvider](../../src/Magpie.Sdk/Integrations/IIndexerFeedProvider.cs) | A configured feed selects a provider that synchronizes indexer definitions. | [Indexer feeds](../development/components/indexer-feeds.md) |
| [IRuntimeIndexerIntegrationFactory](../../src/Magpie.Sdk/Integrations/IRuntimeIndexerIntegrationFactory.cs) | `DefinitionType` selects a factory. When no direct provider matches, `Create(indexer)` makes an integration from the stored schema data. | [Runtime indexers](../development/components/runtime-indexers.md) |
| [IDownloadClientIntegration](../../src/Magpie.Sdk/Integrations/IDownloadClientIntegration.cs) | A configured client selects a provider for submission, status, removal, and connection tests. | [Download clients](../development/components/download-clients.md) |
| [IMediaServerIntegration](../../src/Magpie.Sdk/Integrations/IMediaServerIntegration.cs) | A configured server selects a provider and its supported capabilities. | [Media servers](../development/components/media-servers.md) |
| [IMediaInfoExtractor](../../src/Magpie.Sdk/Media/IMediaInfoExtractor.cs) | `ExtractAsync` returns measured file information, or null. Magpie uses only the last registered extractor. | [Media inspectors](../development/components/media-inspectors.md) |

An installed provider is not a configured connection. Each connection passes its own settings to the selected provider, so providers should not share state between connections.

## Optional capabilities

An optional contract is implemented on the same object as its base contract, which is registered as its guide shows.

| Base contract | Optional contracts | Effect |
| --- | --- | --- |
| `IContentTypeDefinitionProvider` | `IReleaseMatchContentTypeDefinitionProvider`, `IPathResolutionContentTypeDefinitionProvider`, `IFileRoutingContentTypeDefinitionProvider` | Match releases, resolve owned paths, or route files to child items. |
| `IContentHierarchyMetadataSearcher` | `IContentHierarchyMetadataAdvancedSearcher` | Structured criteria, reusable continuation, and candidate hydration. Required for [Add content and wishlist discovery](../development/components/metadata-search.md). |
| `IContentHierarchyMetadataProvider` | `IContentHierarchyMetadataRelationshipProvider` | Add related subjects from a fetched contribution. |
| `IDownloadClientIntegration` | `IPausableDownloadClient`, `IResumableDownloadClient`, `IForcableDownloadClient` | Make the supported transfer controls available. |
| `IMediaServerIntegration` | `IRefreshableMediaServerIntegration`, `ILinkableMediaServerIntegration` | Request a refresh, or return a confirmed link to the server item. |
| Integration or notification provider with settings | `IValidatesIntegrationSettings` | Validate relationships between settings that the field schema cannot express. |

`ICredentialAuthenticationProvider` also extends `IAuthenticationProvider`, but the host reads login providers and request providers from different registrations. Register the class as `ICredentialAuthenticationProvider` for login, and also as `IAuthenticationProvider` if it authenticates requests.

## Host and browser implementation contracts

| Contract | Namespace | Host use |
| --- | --- | --- |
| [IAuthenticationProvider](../../src/Magpie.Sdk/Auth/IAuthenticationProvider.cs) | `Magpie.Sdk.Auth` | Tries request credentials and issues challenges. See [authentication](../development/components/authentication.md). |
| [ICredentialAuthenticationProvider](../../src/Magpie.Sdk/Auth/ICredentialAuthenticationProvider.cs) | `Magpie.Sdk.Auth` | Verifies login credentials. Register it as `ICredentialAuthenticationProvider`. |
| [INotificationProvider](../../src/Magpie.Sdk/Notifications/INotificationProvider.cs) | `Magpie.Sdk.Notifications` | `SendAsync` delivers an event for a rule. See [notifications](../development/components/notifications.md). |
| [IJob](../../src/Magpie.Sdk/Jobs/IJob.cs) | `Magpie.Sdk.Jobs` | Runs a registered job with a `JobContext`. Register it with `RegisterJob<T>(JobSpec)`. See [jobs](../development/components/jobs.md). |
| [IDiagnosticCheck](../../src/Magpie.Sdk/Diagnostics/IDiagnosticCheck.cs), [IDiagnosticRepair](../../src/Magpie.Sdk/Diagnostics/IDiagnosticRepair.cs) | `Magpie.Sdk.Diagnostics` | Observe conditions and make requested corrections. See [diagnostic contracts](diagnostics.md). |
| [IApiEndpointGroup](../../src/Magpie.Sdk/Api/IApiEndpointGroup.cs) | `Magpie.Sdk.Api` | Maps endpoints under `/api/{Name}`. See [API endpoints](../development/components/api-endpoints.md). |
| [IUIExtensionProvider](../../src/Magpie.Sdk/UI/IUIExtensionProvider.cs) | `Magpie.Sdk.UI` | `GetUIExtensions` returns declarations for [UI slots](ui-slots.md). [UI contributions](../development/components/ui-contributions.md) shows a working Web Component. |
| [IAssetProvider](../../src/Magpie.Sdk/UI/IAssetProvider.cs) | `Magpie.Sdk.UI` | `ListAssets` declares resources. `GetAssetStreamAsync` opens a new embedded stream, or returns null. See [UI assets](../development/components/ui-assets.md). |
| [IThemeProvider](../../src/Magpie.Sdk/UI/IThemeProvider.cs) | `Magpie.Sdk.UI` | `GetThemes` returns named, embedded stylesheets with a dark or light variant. See [themes](../development/components/themes.md). |

[JobSpec](../../src/Magpie.Models/JobSpec.cs) defines permissions, triggers, failure policy, parameters, and concurrency. [JobContext](../../src/Magpie.Models/JobContext.cs) contains the execution and trigger IDs, the parameters, the optional event, and the correlation ID.

[UIExtension](../../src/Magpie.Models/UI/UIExtension.cs) has a stable declaration ID and the ID of the owning extension. Ordinary slots accept `Before`, `After`, and `Replace`, tab and navigation slots accept only `After`, and lower `Priority` values come first. See [UI slots](ui-slots.md#declaration-fields).

An [Asset](../../src/Magpie.Models/UI/Asset.cs) has a value in `Url` or in `RelativePath`, not in both. A [ThemeDefinition](../../src/Magpie.Models/UI/ThemeDefinition.cs) has the exact embedded resource name and a `Dark` or `Light` variant.

## Settings and lifecycle

| Contract | Identity and result |
| --- | --- |
| [IExtensionSettingsSchemaProvider](../../src/Magpie.Sdk/Configuration/IExtensionSettingsSchemaProvider.cs) | `GetSettingsGroups` returns `ExtensionSettingsGroupDefinition` values. The extension ID and the group name identify each group. |
| [IExtensionConfigurationTester](../../src/Magpie.Sdk/Configuration/IExtensionConfigurationTester.cs) | `ExtensionId` and `SettingsGroupName` select the group. `TestConfigurationAsync` tests the supplied candidate values and returns a `ConfigurationTestResult`. |
| [IExtensionLifecycle](../../src/Magpie.Sdk/Extensions/IExtensionLifecycle.cs) | Extends the entry point with install, upgrade, application-initialize, disable, and uninstall hooks. |

The settings contracts are in `Magpie.Sdk.Configuration` and the lifecycle contracts in `Magpie.Sdk.Extensions`. See [Settings](../development/components/settings.md) and [Lifecycle](../development/components/lifecycle.md).

[ExtensionLifecycleContext](../../src/Magpie.Sdk/Extensions/ExtensionLifecycleContext.cs) supplies `string NodeId`, `IServiceProvider ServiceProvider`, and `ILoggerFactory LoggerFactory`. The service provider is scoped to the hook, so finish work that uses it before the hook returns.

## Replacement contracts

A replacement contract takes one implementation. The host registers its default only when no extension registered one.

| Contract | Namespace | Required boundary |
| --- | --- | --- |
| `IPasswordPolicy` | `Magpie.Sdk.Security` | [Validation of new local passwords](../development/components/password-policy.md). It does not do hashing or sign-in. |
| `ICacheFactory`, `ICache` | `Magpie.Sdk.Caching` | [Shared and scoped caches](../development/components/caching.md), including freshness, retention, and population policy. |
| `IKeyStore` | `Magpie.Sdk.Security` | [Named authentication keys and HMAC operations](../development/components/key-storage.md). Key identity must stay the same across starts and nodes. |
| `IDataProtectionProvider` | `Magpie.Sdk.Security` | [Protection of stored sensitive values](../development/components/data-protection.md). Existing data and keys must stay readable. |
| `IJobRetryPolicy` | `Magpie.Sdk.Jobs` | [Retry decisions](../development/components/job-retry-policy.md) in the existing job runtime. |
| `IClusterBackplane` | `Magpie.Sdk.Extensions` | [Best-effort event transport](../development/components/cluster-backplane.md) between cluster nodes. |

A backplane does not store events, and the host does not resend a missed one. If `BroadcastEventAsync` throws, the host logs a warning and drops the event. If the listener fails, the node stops receiving peer events until it restarts. A node that misses a job-cancellation event keeps running the execution until the job ends.

## Supported service consumption

Components can call these interfaces to use existing host behavior. They are not replacement points. See [Built-in services](../development/using-built-in-services.md) for working calls.

| Task | Interfaces |
| --- | --- |
| Content, file associations, and metadata | `IContentService`, `IMetadataService`, `IMetadataSearchService`, `ISubjectService`, `ITagService`, `IWishlistService` |
| Acquisition and import | `IReleaseSearchService`, `IDownloadService`, `ILibraryImportService` |
| Configured integrations and storage | `IIndexerService`, `IDownloadClientService`, `IMediaServerService`, `IRootFolderService`, `IRemotePathMappingService`, `IQualityService` |
| Application configuration and state | `ISettingsService`, `IUserService`, `IExtensionService`, `IDiagnosticsService`, `IMagpieSystem` |
| Jobs and progress | `IJobManager`, `IProgressReporter` |
| Events | `IEventBus`, `IEventListener` |
| Shared facilities | `IFileSystem`, `ICacheFactory`, logging, HTTP clients, and `TimeProvider` |

Application services, events, and file-system access are in [Magpie.Sdk.Services](../../src/Magpie.Sdk/Services). Progress is in [Magpie.Sdk.Jobs](../../src/Magpie.Sdk/Jobs). Caching is in [Magpie.Sdk.Caching](../../src/Magpie.Sdk/Caching).

[MagpieEvents](../../src/Magpie.Sdk/Events/MagpieEvents.cs) holds the published event names and context keys, and the [telemetry reference](telemetry.md) lists the instruments.

---

[Reference](README.md) | [Documentation](../README.md)
