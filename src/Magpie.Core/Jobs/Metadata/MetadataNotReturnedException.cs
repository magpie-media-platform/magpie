using System;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class MetadataNotReturnedException(string message) : Exception(message);
