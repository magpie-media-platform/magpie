# Magpie Development Rules

Repository configuration, analyzers, formatters, and linters define all mechanical rules. This file contains only
rules that require judgment.

## Workflow

- Run git write operations only after an explicit user request or during an automated workflow.
- Complete a task only after all relevant gates pass.
  - For backend changes, run `dotnet build src/Magpie.sln -c Release`.
  - Then run `dotnet test src/Magpie.sln -c Release`.
  - For UI changes, run `cd ui && pnpm run verify-full`.
- Fix every gate failure.
- Fix failures outside the current change.
- If a gate cannot pass, report the blocker.
- If the user requests a commit, use `type(scope): summary`.
- Write the commit summary in lowercase and imperative form.
- Do not add a `Co-Authored-By` trailer.

## Frontend

- Use React Router for internal navigation.
- Do not use anchors or `window.location` for SPA routes.
- First, search for an existing component with the same behavior. Then reuse or extend that component.
- Create a component only for new behavior.

## Backend

- Use `internal` for implementation types.
- Use `public` only for API contracts, plugin contracts, abstraction interfaces, models, and public DTOs.
- Use mutable records for entities and domain models.
- Use init-only records for output and result DTOs.
- `Magpie.Models`, `Magpie.Sdk` and `Magpie.Abstractions` target `netstandard2.1`, where `init` puts a
  `modreq(IsExternalInit)` in the setter signature and binds a compiled extension to the polyfill. Give `set` to
  every property that an extension assembly constructs or assigns. Keep `init` only on a type that `Magpie.Core`,
  `Magpie.Api` or `Magpie.Auth` alone constructs. `Magpie.Sdk` and `Magpie.Abstractions` are extension-facing
  throughout, so they use no `init`.
- Do not cache a static default instance of a type with `set` properties. Make the default an expression-bodied
  property so that each caller gets a new instance.
- Add `required` to non-nullable string properties.
- Do not change an existing mutable DTO only for style.
- Do not split a class across files with `partial`. If a class is too large, extract a separate class with its own
  responsibility. Use `partial` only when a source generator requires it, and keep that class in one file.
- When a call, signature, argument list or initializer wraps, put each argument, parameter or property on its own line.

### Repositories

- Use `AsNoTracking()` for repository reads.
- If a repository operation fails, log the exception. Then rethrow the exception.
- For multi-step writes, put the write operations and audit entry in one `ITransactionManager` transaction.
- Do not return `Result<T>` from repositories.

### APIs

- Use minimal endpoint groups and `IResult`.
- Apply this role order: Viewer < Requester < Manager < Admin.
- Require the lowest role that gives sufficient access.
- Do not add MVC controllers.

### Dependency injection

- Put registrations in the service-collection extension for the applicable layer.
- Use `Add*` by default.
- Use `TryAdd*` only for a plugin-replaceable single default.
- Register that default after extension registrations.
- Register contribution seams with `Add*` for `IEnumerable<T>` consumers.

### Comments

- Write public XML documentation for the consumer.
- Write one-line summaries that state what the member is or does.
- Document every parameter.
- Do not use `<remarks>` or `<example>`.
- Use `/// <inheritdoc />` on implementations.
- Use an inline comment only for a non-obvious constraint, invariant, or workaround.
- Do not use comments to describe code or identify method sections.

## Tests

- Use NUnit.
- Use SQLite in-memory for repository tests. Do not use EF Core InMemory.
- Unit tests do not use the physical file system, database migrations, a real host or `TestServer`, or real delays.
  Use fakes, `FakeTimeProvider`, and SQLite in-memory.
- Use `NullLogger` or `NullLoggerFactory`. Do not write to the console or to `TestContext.Out`.
- Name tests `Method_Result_WhenCondition`.
- Use Arrange, Act, and Assert comments.
- Use `Assert.EnterMultipleScope()` for grouped assertions.
- Keep test-side logic near zero.
- Write expected values as literals.
- Configure shared test state once for each class.
- Do not add test factories, builders, or helper methods. They add test-side logic and show that tests require different setup.
- If tests need different setup, use `TestCase` or a data-only `TestCaseSource`.

## Documentation

- Use ASD-STE100 Simplified Technical English for documentation and comments.
