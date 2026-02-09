namespace Magpie.QueryLanguage;

internal sealed record ScalarExprNode(string Field, ScalarOperator Operator, ValueNode? Value) : QueryNode;
