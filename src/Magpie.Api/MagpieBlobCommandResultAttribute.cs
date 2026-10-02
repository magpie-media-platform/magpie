using System;

namespace Magpie.Api;

/// <summary>
/// The attribute that marks a generated Magpie CLI command response as a binary download.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class MagpieBlobCommandResultAttribute : Attribute;
