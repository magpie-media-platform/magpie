namespace Magpie.QueryLanguage;

internal sealed record ItemFieldNode(string Field, ScalarOperator Operator, ValueNode? Value) : ItemExprNode;
