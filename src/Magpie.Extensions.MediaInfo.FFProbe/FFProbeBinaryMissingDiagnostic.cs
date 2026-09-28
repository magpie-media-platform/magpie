using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Extensions.MediaInfo.FFProbe;

internal sealed class FFProbeBinaryMissingDiagnostic : IDiagnosticCheck
{
    private static readonly string[] CandidateNames = ["ffprobe", "ffprobe.exe"];
    public const string Id = "ffprobe.config.binary-missing";

    public string DiagnosticCheckId => Id;

    public string Category => "Media Info";

    private static IReadOnlyList<string> DefaultSearchDirectories()
    {
        var directories = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            directories.AddRange(path!.Split(Path.PathSeparator));
        }

        return directories;
    }

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (BinaryIsResolvable())
        {
            return Task.FromResult<IReadOnlyList<DiagnosticFinding>>([]);
        }

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(
        [
            new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "FFprobe binary not found",
                Detail =
                    "Magpie cannot find ffprobe in its base directory, the current directory or PATH, so it " +
                    "cannot read media information from imported files.",
            },
        ]);
    }

    private bool BinaryIsResolvable()
    {
        foreach (var directory in DefaultSearchDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var name in CandidateNames)
            {
                if (File.Exists(Path.Combine(directory, name)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
