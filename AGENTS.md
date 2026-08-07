# Magpie Development Rules

Coding style and process rules for Magpie.

## Critical Rules

### Git Operations

**Do not run unsolicited git write operations** such as `git add`, `git commit`,
`git push`, or `git tag`. Only commit when explicitly asked by the user or when an
automated workflow requires it as part of its task cycle.

### Build and Tests Must Pass Cleanly

A task is not done until the relevant build and tests pass with zero errors and
zero warnings:

- Backend: `dotnet build src/Magpie.sln -c Release` and `dotnet test`.
- UI: `cd ui && npm run typecheck` (`tsc -b` over app + node + test projects).
  Bare `tsc --noEmit` is a no-op here because the root tsconfig has `"files": []`,
  so it must not be used as the gate.

No gate failure (warning, error, typecheck, lint, format, or test) is
acceptable as "known" or "pre-existing." Fix every failure before marking the task
complete, even one caused by code outside the task; if you genuinely cannot, stop
and report the blocker rather than ignore it.

### Conventional Commits

- Format: `type(scope): summary`. Common types: `feat`, `fix`, `docs`,
  `refactor`, `test`, `chore`. Scope is optional but preferred.
- Imperative mood. All lowercase, including the summary and bullets.
- The summary describes what was accomplished, not the low-level change.
- Optional body uses 2-3 bullets (5 max). Bullets describe how the change
  was made.
- No `Co-Authored-By` trailer.

## Frontend

### SPA Navigation

**NEVER introduce full page reloads.** All navigation must use React Router
(`<Link>`, `<NavLink>`, `useNavigate`). Never use `<a href>` for internal
links or `window.location` for navigation.

### Component Hygiene

Do not create duplicate components. Before adding one, search for an existing
component with the same behavior; reuse it, extend it (props or variants), or factor
the shared behavior into a component both can use. Add a NEW component only for
genuinely new behavior, never a near-copy of an existing one.

## Backend (C#)

Mechanical rules and several house rules are enforced at build time by
`src/.editorconfig` (warnings are errors): file-scoped namespaces, braces-always,
`var`, primary constructors, the naming styles below (including the `Async` suffix),
`.ConfigureAwait(false)` (CA2007), no string interpolation in log messages (CA2254),
PascalCase log placeholders (CA1727), and sealed internal types (CA1852). The
load-bearing rules, which agent-written code most often gets wrong:

- File-scoped namespaces; one top-level type per file; `using`s outside the
  namespace.
- Using order: `System` -> `Magpie` -> `Microsoft` -> third-party -> aliases,
  with a blank line between groups.
- 4-space indent, 120 columns, Allman braces, braces ALWAYS (no brace-less or
  single-line bodies).
- Prefer `var`; collection expressions `[]`; switch expressions and pattern
  matching.
- `internal sealed` by default for implementations; `public` ONLY for real API or
  plugin surface. `Magpie.Abstractions` interfaces, `Magpie.Models`, and public
  DTOs stay public. Primary constructors for DI.
- Naming: `PascalCase` types/members, `IPascalCase` interfaces, `_camelCase`
  private fields, `Async` suffix on async methods.
- Entities and domain models are `record` types with `{ get; set; }`; output/result
  DTOs are `record` types with `{ get; init; }` (immutable after construction; use a
  `with` expression to produce a modified copy). `required` for non-nullable strings.
  Going-forward standard; existing `{ get; set; }` DTOs are not retrofitted.
- `.ConfigureAwait(false)` on every await, in every layer (repositories, services,
  API handlers, host).
- Logging: structured `{Placeholders}` (PascalCase) with Capitalized message text;
  NEVER string interpolation in a log message.
- Repositories: `AsNoTracking()` reads; try/catch -> `LogError` -> rethrow;
  multi-step writes inside `ITransactionManager.ExecuteWithTransactionAsync`; audit
  entry written inside the transaction. No `Result<T>`.
- API: minimal-API groups (`internal sealed XxxEndpointGroup : IApiEndpointGroup`
  with `MapEndpoints`); handlers are `internal async Task<IResult>` returning
  `Results.Ok/Created/NotFound/NoContent`, gated by the role hierarchy
  `.RequireViewer()/.RequireRequester()/.RequireManager()/.RequireAdmin()` (Viewer <
  Requester < Manager < Admin; each requires at least that role). No MVC controllers.
- DI: register in `Magpie<Layer>ServiceCollectionExtensions.Add<Layer>(this IServiceCollection)`
  with `ArgumentNullException.ThrowIfNull` guards; return `services`. Plain
  `Add*` is the default. `TryAdd*` is reserved for the specific slots that need
  it: a plugin-replaceable single-resolve default, registered AFTER extension
  loading, so an extension registration wins and the default only fills the gap.
  Plugin contribution seams are plain `Add*` on interfaces consumed as
  `IEnumerable<T>`.
- XML docs are mandatory on the public surface (CS1591 is enforced as an error):
  every `public` type and member needs a `<summary>`. `internal`/`private` members
  do not (CS1591 does not fire on them). A summary is ONE terse line saying WHAT
  the thing is, addressed to the consumer of the type, never why/how, never a
  paragraph; for a self-evident member a short factual phrase is correct ("Total
  page count.") but it still must be present. Documenting is ALL-OR-NOTHING per
  member: a method gets a `<summary>` AND a `<param>` for EVERY parameter
  (including `cancellationToken`), never a partial set (CS1573 enforces this);
  `<returns>` is optional and usually omitted. Do not use `<remarks>` or `<example>`
  tags (they invite paragraph-style exposition). `<see cref>`/`<see langword>` are
  fine and encouraged where they add value (a validated cross-reference, `true`/
  `false`/`null`). `/// <inheritdoc />` on implementations.
- Inline comments are rare and expensive; default to NONE. The bar is EXTREMELY
  high: write one only for a WHY the code cannot show AND that the surrounding
  names do not already imply (a cited workaround, a surprising external constraint,
  a non-obvious invariant). A comment that narrates WHAT the code does, restates a
  line, sections a method (`// validate`, `// save`), or repeats what a method or
  variable name already says must not exist. When in doubt, delete it. Lowercase.
- Tests: NUnit; repository tests use SQLite in-memory (never EF Core InMemory);
  the SQLite in-memory pattern is scoped to repository tests, not a license for
  databases elsewhere; loggers are `NullLogger`/`NullLoggerFactory`, never a real
  logger and never a mocked one; subject typed as its interface;
  `Method_Result_WhenCondition` names; `// Arrange/Act/Assert`;
  `Assert.EnterMultipleScope()` for grouped assertions.
- Test philosophy, and why helper methods and factories are banned: a test runs
  a scenario and validates output, so test-side logic must be near zero. If
  arranging a scenario takes real work, something is seriously wrong with the
  test or the design under it. If a test computes its expected output
  dynamically, it is reimplementing the code under test in order to test it,
  and verifies nothing; expected values are written literally. Setup happens
  once per test class; when tests need many scenario variants, the variation is
  expressed as `[TestCase]` rows (or a `[TestCaseSource]`, which is data, not a
  helper, and is not banned) over one test body, never as per-test factory or
  builder helpers. Inlining a helper into each test is an improvement over
  the helper, not the goal: the goal is no test-side logic anywhere, wherever
  it sits.

## Writing Docs and Comments

- ASCII only: use `->` in place of the arrow glyph. Never use an em-dash or `--`;
  rephrase with a comma, parentheses, a colon, or a separate sentence.
- Minimal markdown tables: no leading/trailing `|`, single `-` in the header
  separator.
- Prose docs use an impersonal voice: no "you" or "I", no imperative commands.
- No headlinese (sentence fragments without subjects/verbs).
