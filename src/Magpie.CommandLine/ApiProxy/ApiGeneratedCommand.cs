using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.CommandLine.Descriptors;
using Magpie.CommandLine.Dispatch;

using Microsoft.AspNetCore.Builder;

namespace Magpie.CommandLine.ApiProxy;

internal sealed class ApiGeneratedCommand : Command
{
    private readonly MagpieCommandManifestEntry _entry;
    private readonly IReadOnlyDictionary<MagpieCommandParameterDescriptor, Argument<string>> _pathArguments;
    private readonly IReadOnlyDictionary<MagpieCommandParameterDescriptor, Option<string>> _queryOptions;
    private readonly IReadOnlyDictionary<MagpieCommandParameterDescriptor, Option<string[]>> _queryCollectionOptions;
    private readonly Option<string>? _jsonOption;
    private readonly Option<string>? _dataOption;
    private readonly Option<string>? _fileOption;
    private readonly Option<string>? _blobOutputOption;
    private readonly CommandTransportOptionSet _transportOptions;
    private readonly Func<CancellationToken, Task<WebApplication>>? _localAppFactory;

    public ApiGeneratedCommand(
        MagpieCommandManifestEntry entry,
        Func<CancellationToken, Task<WebApplication>>? localAppFactory)
        : base(entry.Command, entry.Description ?? $"Run {entry.Group} {entry.Command}")
    {
        _entry = entry;
        _localAppFactory = localAppFactory;
        _transportOptions = new CommandTransportOptionSet();
        _pathArguments = AddPathArguments(entry.PathParameters);
        _queryOptions = AddQueryOptions(entry.QueryParameters.Where(parameter => !parameter.IsCollection));
        _queryCollectionOptions = AddQueryCollectionOptions(
            entry.QueryParameters.Where(parameter => parameter.IsCollection));

        if (entry.BodyKind is MagpieCommandBodyKind.OptionalJson or MagpieCommandBodyKind.RequiredJson)
        {
            _jsonOption = new Option<string>("--json")
            {
                Description = "Inline JSON request body."
            };
            _dataOption = new Option<string>("--data")
            {
                Description = "Read the JSON request body from a file."
            };
            Options.Add(_jsonOption);
            Options.Add(_dataOption);
        }
        else if (entry.BodyKind == MagpieCommandBodyKind.File)
        {
            _fileOption = new Option<string>("--file")
            {
                Description = "File to upload.",
                Required = true
            };
            Options.Add(_fileOption);
        }

        if (entry.BlobResult)
        {
            _blobOutputOption = new Option<string>("--output", "-o")
            {
                Description = "File path to write the result to; defaults to the server-suggested file name."
            };
            Options.Add(_blobOutputOption);
        }

        _transportOptions.AddTo(this);
        SetAction(ExecuteAsync);
    }

    private async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        CommandHttpRequest httpRequest;
        try
        {
            var request = new ApiCommandRequest(
                _entry.Method,
                _entry.RoutePattern,
                BindPathValues(parseResult),
                BindQueryValues(parseResult),
                await BuildJsonBodyAsync(parseResult, cancellationToken).ConfigureAwait(false),
                BuildFilePath(parseResult),
                _entry.FileFieldName);
            httpRequest = request.ToHttpRequest();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        if (_blobOutputOption is not null)
        {
            return await httpRequest.ExecuteBlobAsync(
                _transportOptions.Bind(parseResult),
                _localAppFactory,
                parseResult.GetValue(_blobOutputOption),
                output,
                error,
                cancellationToken).ConfigureAwait(false);
        }

        return await httpRequest.ExecuteAsync(
            _transportOptions.Bind(parseResult),
            _localAppFactory,
            output,
            error,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> BuildJsonBodyAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (_jsonOption is null || _dataOption is null)
        {
            return null;
        }

        var json = parseResult.GetValue(_jsonOption);
        var data = parseResult.GetValue(_dataOption);
        if (!string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(data))
        {
            throw new InvalidOperationException("Use either --json or --data, not both.");
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        if (!string.IsNullOrWhiteSpace(data))
        {
            var path = data.StartsWith('@') ? data[1..] : data;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("--data must reference a JSON file.");
            }

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        if (_entry.BodyKind == MagpieCommandBodyKind.RequiredJson)
        {
            throw new InvalidOperationException("Provide --json or --data for this request body.");
        }

        return null;
    }

    private string? BuildFilePath(ParseResult parseResult)
    {
        if (_fileOption is null)
        {
            return null;
        }

        var path = parseResult.GetValue(_fileOption);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("The upload file was not found.", path);
        }

        return Path.GetFullPath(path);
    }

    private IReadOnlyDictionary<MagpieCommandParameterDescriptor, Argument<string>> AddPathArguments(
        IEnumerable<MagpieCommandParameterDescriptor> parameters)
    {
        var arguments = new Dictionary<MagpieCommandParameterDescriptor, Argument<string>>();
        foreach (var parameter in parameters)
        {
            var argument = new Argument<string>(parameter.CliName)
            {
                Arity = ArgumentArity.ExactlyOne,
                Description = $"Route value for {parameter.Name}."
            };
            argument.Validators.Add(result =>
            {
                if (result.Tokens.Count > 0 && string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                {
                    result.AddError($"Required argument missing for command: '{Name}'.");
                }
            });
            Arguments.Add(argument);
            arguments[parameter] = argument;
        }

        return arguments;
    }

    private IReadOnlyDictionary<MagpieCommandParameterDescriptor, Option<string>> AddQueryOptions(
        IEnumerable<MagpieCommandParameterDescriptor> parameters)
    {
        var options = new Dictionary<MagpieCommandParameterDescriptor, Option<string>>();
        foreach (var parameter in parameters)
        {
            var option = new Option<string>(parameter.CliName)
            {
                Description = $"Query value for {parameter.Name}."
            };
            Options.Add(option);
            options[parameter] = option;
        }

        return options;
    }

    private IReadOnlyDictionary<MagpieCommandParameterDescriptor, Option<string[]>> AddQueryCollectionOptions(
        IEnumerable<MagpieCommandParameterDescriptor> parameters)
    {
        var options = new Dictionary<MagpieCommandParameterDescriptor, Option<string[]>>();
        foreach (var parameter in parameters)
        {
            var option = new Option<string[]>(parameter.CliName)
            {
                Description = $"Query values for {parameter.Name}."
            };
            Options.Add(option);
            options[parameter] = option;
        }

        return options;
    }

    private IReadOnlyDictionary<string, string> BindPathValues(ParseResult parseResult)
    {
        return _pathArguments.ToDictionary(
            entry => entry.Key.Name,
            entry => parseResult.GetValue(entry.Value) ?? string.Empty,
            StringComparer.Ordinal);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> BindQueryValues(ParseResult parseResult)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (parameter, option) in _queryOptions)
        {
            if (parseResult.GetValue(option) is { } value)
            {
                values[parameter.Name] = [value];
            }
        }

        foreach (var (parameter, option) in _queryCollectionOptions)
        {
            var optionValues = parseResult.GetValue(option) ?? [];
            if (optionValues.Length > 0)
            {
                values[parameter.Name] = optionValues;
            }
        }

        return values;
    }
}
