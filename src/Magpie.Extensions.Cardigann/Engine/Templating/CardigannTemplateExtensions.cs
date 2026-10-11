using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Magpie.Extensions.Cardigann.Engine.Templating;

internal static class CardigannTemplateExtensions
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex ReReplaceRegex = new(
        @"{{\s*re_replace\s+(\..+?)\s+""(.*?)""\s+""(.*?)""\s*}}",
        RegexOptions.Compiled);

    private static readonly Regex ReReplacePipeRegex = new(
        @"{{\s*(\..+?)\s*\|\s*re_replace\s+""(.*?)""\s+""(.*?)""\s*}}",
        RegexOptions.Compiled);

    private static readonly Regex JoinRegex = new(@"{{\s*join\s+(\..+?)\s+""(.*?)""\s*}}", RegexOptions.Compiled);

    private static readonly Regex JoinPipeRegex = new(
        @"{{\s*(\..+?)\s*\|\s*join\s+""(.*?)""\s*}}",
        RegexOptions.Compiled);

    private static readonly Regex LogicFunctionRegex = new(
        @"\b(and|or|eq|ne)(?:\s+(\(?\.[^\)\s]+\)?|""[^""]+""))" + "{2,}",
        RegexOptions.Compiled);

    private static readonly Regex IfElseRegex = new(
        @"{{\s*if\s*(.+?)\s*}}(.*?){{\s*else\s*}}(.*?){{\s*end\s*}}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex IfRegex = new(
        @"{{\s*if\s*(.+?)\s*}}(.*?){{\s*end\s*}}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RangeRegex = new(
        @"{{\s*range\s*(((?<index>\$.+?),)((\s*(?<element>.+?)\s*(:=)\s*)))?(?<variable>.+?)\s*}}" +
        @"(?<prefix>.*?){{\.}}(?<postfix>.*?){{\s*end\s*}}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SimpleVariableRegex = new(@"{{\s*(\..+?)\s*}}", RegexOptions.Compiled);

    internal const string TrueVar = ".True";

    private const string FalseVar = ".False";

    public static string RenderCardigannTemplate(
        this string template,
        IReadOnlyDictionary<string, object?> variables,
        Func<string, string>? escapeValue = null)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{", StringComparison.Ordinal))
        {
            return template;
        }

        var vars = new Dictionary<string, object?>(variables);
        vars.TryAdd(TrueVar, "True");
        vars.TryAdd(FalseVar, null);

        template = ApplyReReplace(template, vars, escapeValue);
        template = ApplyJoin(template, vars, escapeValue);
        template = ApplyLogicFunctions(template, vars);
        template = ApplyConditionals(template, vars);
        template = ApplyRange(template, vars, escapeValue);
        template = ApplySimpleVariables(template, vars, escapeValue);

        return template;
    }

    private static string Escape(string value, Func<string, string>? escapeValue)
    {
        return escapeValue is null ? value : escapeValue(value);
    }

    private static string ApplyReReplace(
        string template,
        IDictionary<string, object?> vars,
        Func<string, string>? escapeValue)
    {
        Func<Match, string> evaluate = m => ReReplace(
            GetString(vars, m.Groups[1].Value),
            m.Groups[2].Value,
            m.Groups[3].Value,
            m.Value,
            escapeValue);

        return ReplaceAll(ReplaceAll(template, ReReplacePipeRegex, evaluate), ReReplaceRegex, evaluate);
    }

    private static string ReReplace(
        string input,
        string pattern,
        string replacement,
        string original,
        Func<string, string>? escapeValue)
    {
        try
        {
            return Escape(
                Regex.Replace(input ?? string.Empty, pattern, replacement, RegexOptions.None, RegexTimeout),
                escapeValue);
        }
        catch (RegexMatchTimeoutException)
        {
            return original;
        }
        catch (ArgumentException)
        {
            return original;
        }
    }

    private static string ApplyJoin(
        string template,
        IDictionary<string, object?> vars,
        Func<string, string>? escapeValue)
    {
        Func<Match, string> evaluate = m => string.Join(
            m.Groups[2].Value,
            GetList(vars, m.Groups[1].Value).Select(value => Escape(value, escapeValue)));

        return ReplaceAll(ReplaceAll(template, JoinPipeRegex, evaluate), JoinRegex, evaluate);
    }

    private static string ApplyLogicFunctions(string template, IDictionary<string, object?> vars)
    {
        var match = LogicFunctionRegex.Match(template);
        var guard = 0;

        while (match.Success && guard++ < 10000)
        {
            var functionStartIndex = match.Groups[0].Index;
            var functionLength = match.Groups[0].Length;
            var functionName = match.Groups[1].Value;
            var parameters = match.Groups[2].Captures
                .Cast<Capture>()
                .Select(c => c.Value.Trim('(', ')'))
                .ToList();
            var usesLiterals = functionName is "eq" or "ne";
            if (!usesLiterals)
            {
                parameters.RemoveAll(p => p.StartsWith("\"", StringComparison.Ordinal));
            }

            var functionResult = string.Empty;

            switch (functionName)
            {
                case "and":
                case "or":
                    {
                        var isAnd = functionName == "and";
                        foreach (var parameter in parameters)
                        {
                            functionResult = parameter;
                            var isFalsy = string.IsNullOrWhiteSpace(GetVariable(vars, parameter) as string);
                            if (isFalsy == isAnd)
                            {
                                break;
                            }
                        }

                        break;
                    }

                case "eq":
                case "ne":
                    {
                        var wantEqual = functionName == "eq";
                        if (parameters.Count > 2)
                        {
                            functionLength = match.Groups[2].Captures[2].Index - functionStartIndex;
                        }

                        var resolved = parameters
                            .Take(2)
                            .Select(p => p.StartsWith("\"", StringComparison.Ordinal)
                                ? p.Trim('"')
                                : GetVariable(vars, p) as string);

                        var isEqual = resolved.Distinct().Count() == 1;
                        functionResult = isEqual == wantEqual ? TrueVar : FalseVar;
                        break;
                    }
            }

            template = template.Remove(functionStartIndex, functionLength).Insert(functionStartIndex, functionResult);
            match = LogicFunctionRegex.Match(template);
        }

        return template;
    }

    private static string ApplyConditionals(string template, IDictionary<string, object?> vars)
    {
        template = ReplaceAll(
            template,
            IfElseRegex,
            m =>
            {
                var condition = m.Groups[1].Value;
                var onTrue = m.Groups[2].Value;
                var onFalse = m.Groups[3].Value;

                if (!TryEvaluateCondition(condition, vars, out var result))
                {
                    return m.Value;
                }

                return result ? onTrue : onFalse;
            });

        template = ReplaceAll(
            template,
            IfRegex,
            m =>
            {
                var condition = m.Groups[1].Value;
                var onTrue = m.Groups[2].Value;

                if (!TryEvaluateCondition(condition, vars, out var result))
                {
                    return m.Value;
                }

                return result ? onTrue : string.Empty;
            });

        return template;
    }

    private static bool TryEvaluateCondition(string condition, IDictionary<string, object?> vars, out bool result)
    {
        result = false;
        if (condition == null || !condition.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        result = IsTruthy(GetVariable(vars, condition));
        return true;
    }

    private static string ApplyRange(
        string template,
        IDictionary<string, object?> vars,
        Func<string, string>? escapeValue)
    {
        return ReplaceAll(
            template,
            RangeRegex,
            m =>
            {
                var index = m.Groups["index"].Success ? m.Groups["index"].Value : null;
                var variable = m.Groups["variable"].Value;
                var prefix = m.Groups["prefix"].Value;
                var postfix = m.Groups["postfix"].Value;

                var list = GetList(vars, variable);
                var indexReplace = "{{" + index + "}}";

                var expanded = string.Empty;
                var arrayIndex = 0;
                foreach (var value in list)
                {
                    var indexValue = arrayIndex++;
                    if (index != null)
                    {
                        var indexText = indexValue.ToString(CultureInfo.InvariantCulture);
                        expanded += prefix.Replace(indexReplace, indexText, StringComparison.Ordinal) +
                            Escape(value, escapeValue) +
                            postfix.Replace(indexReplace, indexText, StringComparison.Ordinal);
                    }
                    else
                    {
                        expanded += prefix + Escape(value, escapeValue) + postfix;
                    }
                }

                return expanded;
            });
    }

    private static string ApplySimpleVariables(
        string template,
        IDictionary<string, object?> vars,
        Func<string, string>? escapeValue)
    {
        return ReplaceAll(
            template,
            SimpleVariableRegex,
            m =>
            {
                var variable = m.Groups[1].Value;
                return Escape(GetString(vars, variable), escapeValue);
            });
    }

    private static string ReplaceAll(string input, Regex regex, Func<Match, string> replacer)
    {
        return regex.Replace(
            input,
            m =>
            {
                try
                {
                    return replacer(m) ?? string.Empty;
                }
                catch (Exception)
                {
                    return m.Value;
                }
            });
    }

    private static object? GetVariable(IDictionary<string, object?> vars, string path)
    {
        if (path != null && vars.TryGetValue(path, out var value))
        {
            return value;
        }

        return null;
    }

    private static string GetString(IDictionary<string, object?> vars, string path)
    {
        var value = GetVariable(vars, path);
        return value switch
        {
            null => string.Empty,
            string s => s,
            IEnumerable<string> list => string.Join(",", list),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IEnumerable<string> GetList(IDictionary<string, object?> vars, string path)
    {
        var value = GetVariable(vars, path);
        return value switch
        {
            null => [],
            IEnumerable<string> list => list,
            string s => [s],
            IEnumerable other => other.Cast<object?>().Select(o => o?.ToString() ?? string.Empty),
            _ => []
        };
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count > 0,
            IEnumerable e => e.Cast<object?>().Any(),
            _ => true
        };
    }
}
