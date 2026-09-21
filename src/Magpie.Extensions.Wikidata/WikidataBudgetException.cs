using System;

namespace Magpie.Extensions.Wikidata;

internal sealed class WikidataBudgetException(string message) : InvalidOperationException(message);
