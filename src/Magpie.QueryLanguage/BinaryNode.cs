namespace Magpie.QueryLanguage;

internal sealed record BinaryNode(BinaryOperator Operator, QueryNode Left, QueryNode Right) : QueryNode;
