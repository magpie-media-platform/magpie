; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
MCLI001 | Magpie.CommandLine | Error | A route parameter has no matching handler parameter.
MCLI002 | Magpie.CommandLine | Error | A command has multiple body parameters.
MCLI003 | Magpie.CommandLine | Error | A command parameter has ambiguous binding.
MCLI004 | Magpie.CommandLine | Error | A group and command pair is duplicated.
MCLI005 | Magpie.CommandLine | Error | A command uses an unsupported HTTP method.
MCLI006 | Magpie.CommandLine | Error | A command option conflicts with a reserved option.
