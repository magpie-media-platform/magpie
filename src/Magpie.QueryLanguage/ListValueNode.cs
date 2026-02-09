using System.Collections.Generic;

namespace Magpie.QueryLanguage;

internal sealed record ListValueNode(IReadOnlyList<ValueNode> Values) : ValueNode;
