namespace Magpie.QueryLanguage;

internal sealed record ItemBinaryNode(BinaryOperator Operator, ItemExprNode Left, ItemExprNode Right) : ItemExprNode;
