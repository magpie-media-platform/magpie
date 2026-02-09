namespace Magpie.QueryLanguage;

internal sealed record CollectionExprNode(string Field, CollectionOperator Operator, ItemExprNode Item) : QueryNode;
