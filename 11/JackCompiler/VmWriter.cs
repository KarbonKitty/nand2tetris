using System.Data.Common;

namespace JackCompiler;

public class VmWriter
{
    public ParseNode ParseTree { get; set; }

    public List<string> VmCode { get; set; } = [];

    private int LabelIndex { get; set; } = 0;
    private string ClassName { get; set; }

    public VmWriter(ParseNode rootNode)
    {
        ParseTree = rootNode;
    }

    // TODO: stop emitting nodes that we are ignoring anyway?
    public void WriteVmCode()
    {
        ProcessClassNode(ParseTree);
    }

    private void ProcessClassNode(ParseNode node)
    {
        ClassName = node.SubNodes.First(n => n is Identifier).Value!.Value;
        foreach (var n in node.SubNodes.Where(n => n.Type == ParseNodeType.subroutineDec))
        {
            ProcessSubroutineDeclarationNode(n);
        }
    }

    private void ProcessSubroutineDeclarationNode(ParseNode node)
    {
        var id = node.SubNodes.First(n => n.Type == ParseNodeType.identifier);

        var subroutineBody = node.SubNodes.First(n => n.Type == ParseNodeType.subroutineBody);
        ProcessSubroutineBodyNode(subroutineBody, $"{ClassName}.{id.Value!.Value}");
    }

    private void ProcessSubroutineBodyNode(ParseNode node, string identifier)
    {
        var varDecs = node.SubNodes.Where(n => n.Type == ParseNodeType.varDec);
        var varCount = varDecs.Sum(n => n.SubNodes.Count(sn => sn is Identifier));
        VmCode.Add($"function {identifier} {varCount}");
        var statements = node.SubNodes.First(n => n.Type == ParseNodeType.statements);
        ProcessStatementsNode(statements);
    }
    
    private void ProcessStatementsNode(ParseNode node)
    {
        foreach (var statementNode in node.SubNodes)
        {
            if (statementNode.Type == ParseNodeType.doStatement)
            {
                ProcessDoStatementNode(statementNode);
            }
            else if (statementNode.Type == ParseNodeType.returnStatement)
            {
                ProcessReturnStatementNode(statementNode);
            }
            else if (statementNode.Type == ParseNodeType.letStatement)
            {
                ProcessLetStatementNode(statementNode);
            }
            else if (statementNode.Type == ParseNodeType.whileStatement)
            {
                ProcessWhileStatmentNode(statementNode);
            }
            else if (statementNode.Type == ParseNodeType.ifStatement)
            {
                ProcessIfStatementNode(statementNode);
            }
            else
            {
                throw new NotImplementedException("handle other statements");
            }
        }
    }

    private void ProcessWhileStatmentNode(ParseNode node)
    {
        var startLabel = $"STARTWHILE{LabelIndex++}";
        var endLabel = $"ENDWHILE{LabelIndex++}";
        VmCode.Add($"label {startLabel}");
        ProcessExpression(node.SubNodes.First(n => n.Type == ParseNodeType.expression));
        VmCode.Add("not");
        VmCode.Add($"if-goto {endLabel}");
        ProcessStatementsNode(node.SubNodes.First(n => n.Type == ParseNodeType.statements));
        VmCode.Add($"goto {startLabel}");
        VmCode.Add($"label {endLabel}");
    }

    private void ProcessIfStatementNode(ParseNode node)
    {
        var elseLabel = $"ELSE{LabelIndex++}";
        var endIfLabel = $"ENDIF{LabelIndex++}";
        ProcessExpression(node.SubNodes.First(n => n.Type == ParseNodeType.expression));
        VmCode.Add("not");
        VmCode.Add($"if-goto {elseLabel}");
        ProcessStatementsNode(node.SubNodes.First(n => n.Type == ParseNodeType.statements));
        VmCode.Add($"goto {endIfLabel}");
        VmCode.Add($"label {elseLabel}");
        ProcessStatementsNode(node.SubNodes.Where(n => n.Type == ParseNodeType.statements).Skip(1).First());
        VmCode.Add($"label {endIfLabel}");
    }

    private void ProcessLetStatementNode(ParseNode node)
    {
        var identifierNode = node.SubNodes[1] as Identifier; // skip 'let'
        ProcessExpression(node.SubNodes[3]); // skip '='
        var memorySegment = IdentifierCategoryTranslator(identifierNode!.Category);
        VmCode.Add($"pop {memorySegment} {identifierNode.Index}");
    }

    private void ProcessDoStatementNode(ParseNode node)
    {
        // extract identifier
        var identifier = node.SubNodes[1].Value!.Value;
        if (node.SubNodes[2] is { Type: ParseNodeType.symbol, Value.Value: "." })
        {
            identifier = identifier + "." + node.SubNodes[3].Value!.Value;
        }
        var expressionList = node.SubNodes.First(n => n.Type == ParseNodeType.expressionList);
        var argCount = ProcessExpressionList(expressionList);
        // emit call
        VmCode.Add($"call {identifier} {argCount}");
    }

    private void ProcessReturnStatementNode(ParseNode node)
    {
        if (node.SubNodes.Any(n => n.Type == ParseNodeType.expression))
        {
            ProcessExpression(node.SubNodes.First(n => n.Type == ParseNodeType.expression));
        }
        else
        {
            VmCode.Add("push constant 0");
        }
        VmCode.Add("return");
    }

    private int ProcessExpressionList(ParseNode node)
    {
        var count = node.SubNodes.Count(n => n.Type == ParseNodeType.expression);
        foreach (var n in node.SubNodes)
        {
            if (n.Type == ParseNodeType.expression)
            {
                ProcessExpression(n);
            }
        }
        return count;
    }

    private void ProcessExpression(ParseNode node)
    {
        if (node.SubNodes.Where(n => n.Type == ParseNodeType.term).Count() > 1)
        {
            // TODO: we assume that this means 'exp op exp'
            var operatorVmCode = TranslateOperator(node.SubNodes.First(n => n.Type == ParseNodeType.symbol).Value!.Value);
            ProcessTerm(node.SubNodes.First(n => n.Type == ParseNodeType.term));
            ProcessTerm(node.SubNodes.Where(n => n.Type == ParseNodeType.term).Skip(1).First());
            VmCode.Add(operatorVmCode);
        }
        else {
            ProcessTerm(node.SubNodes.First());
        }
    }

    private void ProcessTerm(ParseNode node)
    {
        var firstNode = node.SubNodes.First();
        if (firstNode.Type == ParseNodeType.intConst)
        {
            VmCode.Add($"push constant {firstNode.Value!.Value}");
        }
        else if (firstNode is Identifier id)
        {
            if (id.Category == IdentifierCategory.Class)
            {
                // subroutine call
                // extract identifier
                var identifier = firstNode.Value!.Value;
                if (node.SubNodes[1] is { Type: ParseNodeType.symbol, Value.Value: "." })
                {
                    identifier = identifier + "." + node.SubNodes[2].Value!.Value;
                }
                var expressionList = node.SubNodes.First(n => n.Type == ParseNodeType.expressionList);
                var argCount = ProcessExpressionList(expressionList);
                // emit call
                VmCode.Add($"call {identifier} {argCount}");
            }
            else
            {
                VmCode.Add($"push {IdentifierCategoryTranslator(id.Category)} {id.Index}");
            }
        }
        else if (firstNode is { Type: ParseNodeType.symbol, Value.Value: "(" })
        {
            ProcessExpression(node.SubNodes.Skip(1).First());
        }
        else if (firstNode is { Type: ParseNodeType.symbol, Value.Value: "-" })
        {
            ProcessTerm(node.SubNodes.First(n => n.Type == ParseNodeType.term));
            VmCode.Add("neg");
        }
        else if (firstNode is { Type: ParseNodeType.symbol, Value.Value: "~" })
        {
            ProcessTerm(node.SubNodes.First(n => n.Type == ParseNodeType.term));
            VmCode.Add("not");
        }
        else if (firstNode is { Type: ParseNodeType.keyword, Value.Value: "true" })
        {
            VmCode.Add("push constant 1");
            VmCode.Add("neg");
        }
        else if (firstNode is { Type: ParseNodeType.keyword, Value.Value: "false" })
        {
            VmCode.Add("push constant 0");
        }
        else
        {
            throw new NotImplementedException("unexpected node when processing term");
        }
    }

    private string IdentifierCategoryTranslator(IdentifierCategory category)
    {
        return category switch {
            IdentifierCategory.Field => "this",
            IdentifierCategory.Static => "static",
            IdentifierCategory.Var => "local",
            IdentifierCategory.Arg => "argument",
            _ => throw new InvalidOperationException("invalid identifier category")
        };
    }

    private string TranslateOperator(string op)
    => op switch {
        "*" => "call Math.multiply 2",
        "+" => "add",
        "&" => "and",
        "=" => "eq",
        ">" => "gt",
        "-" => "sub",
        _ => throw new Exception("TODO")
    };
}
