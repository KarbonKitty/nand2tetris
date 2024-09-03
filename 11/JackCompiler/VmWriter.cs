using System.Data.Common;

namespace JackCompiler;

public class VmWriter
{
    public ParseNode ParseTree { get; set; }

    public List<string> VmCode { get; set; } = [];

    private int LabelIndex { get; set; } = 0;
    private string ClassName { get; set; }
    private int NumFields { get; set; }

    public VmWriter(ParseNode rootNode)
    {
        ParseTree = rootNode;
    }

    public void WriteVmCode()
    {
        ProcessClassNode(ParseTree);
    }

    private void ProcessClassNode(ParseNode node)
    {
        ClassName = node.SubNodes.First(n => n is Identifier).Value!.Value;
        var fieldsCount = node.SubNodes.Where(n => n.Type == ParseNodeType.classVarDec).Sum(n => n.SubNodes.Count(n => n is Identifier));
        NumFields = fieldsCount;
        foreach (var n in node.SubNodes.Where(n => n.Type == ParseNodeType.subroutineDec))
        {
            ProcessSubroutineDeclarationNode(n);
        }
    }

    private void ProcessSubroutineDeclarationNode(ParseNode node)
    {
        var isConstructor = node.SubNodes.First().Value!.Value == "constructor";
        var isMethod = node.SubNodes.First().Value!.Value == "method";

        var id = node.SubNodes[2];

        var subroutineBody = node.SubNodes.First(n => n.Type == ParseNodeType.subroutineBody);
        ProcessSubroutineBodyNode(subroutineBody, $"{ClassName}.{id.Value!.Value}", isConstructor, isMethod);
    }

    private void ProcessSubroutineBodyNode(ParseNode node, string identifier, bool isConstructor, bool isMethod)
    {
        var varDecs = node.SubNodes.Where(n => n.Type == ParseNodeType.varDec);
        var varCount = varDecs.Sum(n => n.SubNodes.Count(sn => sn is Identifier));
        VmCode.Add($"function {identifier} {varCount}");
        if (isConstructor)
        {
            VmCode.Add($"push constant {NumFields}");
            VmCode.Add("call Memory.alloc 1");
            VmCode.Add("pop pointer 0");
        }
        else if (isMethod)
        {
            VmCode.Add("push argument 0");
            VmCode.Add("pop pointer 0");
        }
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
        if (node.SubNodes.Any(n => n is { Type: ParseNodeType.keyword, Value.Value: "else" }))
        {
            ProcessStatementsNode(node.SubNodes.Where(n => n.Type == ParseNodeType.statements).Skip(1).First());
        }
        VmCode.Add($"label {endIfLabel}");
    }

    private void ProcessLetStatementNode(ParseNode node)
    {
        var identifierNode = node.SubNodes[1] as Identifier; // skip 'let'
        if (node.SubNodes[2] is { Value.Value: "=" })
        {
            ProcessExpression(node.SubNodes[3]); // skip '='
            var memorySegment = IdentifierCategoryTranslator(identifierNode!.Category);
            VmCode.Add($"pop {memorySegment} {identifierNode.Index}");
        }
        else if (node.SubNodes[2] is { Value.Value: "[" }) // array access
        {
            VmCode.Add($"push {IdentifierCategoryTranslator(identifierNode!.Category)} {identifierNode.Index}");
            ProcessExpression(node.SubNodes[3]);
            VmCode.Add("add");
            ProcessExpression(node.SubNodes[6]); // we skip over irrelevant symbols
            VmCode.Add("pop temp 0"); // save the right hand value
            VmCode.Add("pop pointer 1"); // THAT
            VmCode.Add("push temp 0"); // retrieve the saved value
            VmCode.Add("pop that 0"); // save the right hand value to the correct location
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    private void ProcessDoStatementNode(ParseNode node)
    {
        ProcessTerm(node with { SubNodes = node.SubNodes[1..] });
        VmCode.Add("pop temp 0"); // ignore return value
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
            if (node.SubNodes.Any(n => n is { Value.Value: "(" }))
            {
                // this is a subroutine call
                // extract identifier
                string identifier;
                var isMethod = false;
                var isTwoPartId = node.SubNodes[1] is { Type: ParseNodeType.symbol, Value.Value: "." };
                var isFirstPartClassName = id.Category == IdentifierCategory.Class;
                if (!isTwoPartId)
                {
                    identifier = $"{ClassName}.{id.Value!.Value}";
                    VmCode.Add($"push pointer 0");
                    isMethod = true;
                }
                else if (isFirstPartClassName)
                {
                    identifier = $"{id.Value!.Value}.{node.SubNodes[2].Value!.Value}";
                }
                else
                {
                    var relevantClassName = id.VarType;
                    identifier = $"{relevantClassName}.{node.SubNodes[2].Value!.Value}";
                    VmCode.Add($"push {IdentifierCategoryTranslator(id.Category)} {id.Index}");
                    isMethod = true;
                }
                var expressionList = node.SubNodes.First(n => n.Type == ParseNodeType.expressionList);
                var argCount = ProcessExpressionList(expressionList);
                // emit call
                VmCode.Add($"call {identifier} {argCount + (isMethod ? 1 : 0)}");
            }
            else if (node.SubNodes.Any(n => n is { Value.Value: "[" }))
            {
                // this is an array access
                VmCode.Add($"push {IdentifierCategoryTranslator(id.Category)} {id.Index}");
                ProcessExpression(node.SubNodes.First(n => n is { Type: ParseNodeType.expression }));
                VmCode.Add("add");
                VmCode.Add("pop pointer 1");
                VmCode.Add("push that 0");
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
        else if (firstNode is { Type: ParseNodeType.keyword, Value.Value: "this" })
        {
            if (node.SubNodes.Count > 2 && node.SubNodes[1].Value?.Value == ".")
            {
                throw new NotImplementedException();
            }
            else
            {
                VmCode.Add("push pointer 0");
            }
        }
        else if (firstNode is { Type: ParseNodeType.stringConst })
        {
            var strLength = firstNode.Value!.Value.Length;
            VmCode.Add($"push constant {strLength}");
            VmCode.Add("call String.new 1");
            foreach (var c in firstNode.Value!.Value)
            {
                VmCode.Add($"push constant {(byte)c}");
                VmCode.Add("call String.appendChar 2");
            }
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
        "<" => "lt",
        "-" => "sub",
        "/" => "call Math.divide 2",
        _ => throw new Exception("TODO")
    };
}
