namespace JackCompiler;

public class CompilationEngine
{
    public required List<Token> Tokens { get; init; }

    public ParseNode? RootNode { get; set; }

    private SymbolTable ClassLevelTable = new SymbolTable();
    private SymbolTable? SubroutineLevelTable;

    private int CurrentIndex = 0;

    private static readonly Dictionary<TokenType, ParseNodeType> TypeDict = new() {
        { TokenType.keyword, ParseNodeType.keyword },
        { TokenType.intConst, ParseNodeType.intConst },
        { TokenType.stringConst, ParseNodeType.stringConst },
        { TokenType.symbol, ParseNodeType.symbol },
        { TokenType.identifier, ParseNodeType.identifier }
    };

    public void Compile()
    {
        // TODO: allow multiple classes per file?
        RootNode = CompileClass();
    }

    private ParseNode CompileClass()
    {
        ClassLevelTable = new SymbolTable();
        return new()
        {
            Type = ParseNodeType.@class,
            SubNodes = [
                    ConsumeToken(TokenType.keyword, "class"),
                    ConsumeNewIdentifier(IdentifierCategory.Class, string.Empty), // type will be extracted in the ConsumeNewIdentifier method
                    ConsumeToken(TokenType.symbol, "{"),
                    ..CompileZeroOrMore(TryCompileClassVarDec),
                    ..CompileZeroOrMore(TryCompileSubroutineDec),
                    ConsumeToken(TokenType.symbol, "}"),
                ],
            Value = null
        };
    }

    private IEnumerable<ParseNode> CompileZeroOrMore(Func<ParseNode?> compilationRule)
    {
        var compiledNode = compilationRule();
        while (compiledNode is not null)
        {
            yield return compiledNode;
            compiledNode = compilationRule();
        }
    }

    private ParseNode? TryCompileClassVarDec()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.keyword && (currentToken.Value == "static" || currentToken.Value == "field"))
        {
            return CompileClassVarDec();
        }
        return null;
    }

    private ParseNode CompileClassVarDec()
    {
        var varTypeParseNode = ConsumeKeyword(); // either 'static' or 'field'
        var isStatic = varTypeParseNode.Value!.Value == "static";
        var subNodes = new List<ParseNode>();
        subNodes.Add(varTypeParseNode);
        var typeNode = ConsumeToken(); // whatever type we allow
        subNodes.Add(typeNode);
        return new()
        {
            Type = ParseNodeType.classVarDec,
            SubNodes = [
                ..subNodes,
                ConsumeNewIdentifier(isStatic ? IdentifierCategory.Static : IdentifierCategory.Field, typeNode.Value!.Value),
                ..ConsumeZeroOrMoreVarNames(isStatic ? IdentifierCategory.Static : IdentifierCategory.Field, typeNode.Value!.Value),
                ConsumeToken(TokenType.symbol, ";")
                ],
            Value = null
        };
    }

    private IEnumerable<ParseNode> ConsumeZeroOrMoreParameters()
    {
        var parameterIndex = 0;
        while (Peek() is { Type: TokenType.symbol, Value: "," })
        {
            yield return ConsumeSymbol(",");
            var t = ConsumeToken();
            yield return t; // type
            yield return ConsumeNewIdentifier(IdentifierCategory.Arg, t.Value!.Value);
            parameterIndex++;
        }
    }

    private IEnumerable<ParseNode> ConsumeZeroOrMoreVarNames(IdentifierCategory category, string varType)
    {
        while (Peek() is { Type: TokenType.symbol, Value: "," })
        {
            yield return ConsumeSymbol(",");
            yield return ConsumeNewIdentifier(category, varType);
        }
    }

    private ParseNode? TryCompileSubroutineDec()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.keyword && (
            currentToken.Value is "constructor" or "function" or "method"
        ))
        {
            return CompileSubroutineDec();
        }
        return null;
    }

    private ParseNode CompileSubroutineDec()
    {
        SubroutineLevelTable = new SymbolTable();
        var isMethod = Peek() is { Type: TokenType.keyword, Value: "method" };
        if (isMethod)
        {
            SubroutineLevelTable.Define("this", "className", SymbolKind.Arg);
        }
        var subroutineKindNode = ConsumeKeyword();
        var typeNode = ConsumeToken();
        return new ParseNode
        {
            Type = ParseNodeType.subroutineDec,
            SubNodes = [
                subroutineKindNode,
                typeNode, // void or type
                ConsumeNewIdentifier(IdentifierCategory.Subroutine, typeNode.Value!.Value),
                ConsumeToken(TokenType.symbol, "("),
                CompileParameterList(),
                ConsumeToken(TokenType.symbol, ")"),
                CompileSubroutineBody()
            ]
        };
    }

    private ParseNode CompileSubroutineBody()
    {
        return new ParseNode
        {
            Type = ParseNodeType.subroutineBody,
            SubNodes = [
                ConsumeToken(TokenType.symbol, "{"),
                ..CompileZeroOrMore(TryCompileVarDec),
                CompileStatements(),
                ConsumeToken(TokenType.symbol, "}")
            ]
        };
    }

    private ParseNode? TryCompileVarDec()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.keyword && currentToken.Value == "var")
        {
            return CompileVarDec();
        }
        return null;
    }

    private ParseNode CompileVarDec()
    {
        var varNode = ConsumeKeyword("var");
        var typeNode = ConsumeToken();
        return new()
        {
            Type = ParseNodeType.varDec,
            SubNodes = [
                varNode,
                typeNode,
                ConsumeNewIdentifier(IdentifierCategory.Var, typeNode.Value!.Value),
                ..ConsumeZeroOrMoreVarNames(IdentifierCategory.Var, typeNode.Value!.Value),
                ConsumeSymbol(";")
                ]
        };
    }

    private ParseNode CompileStatements()
    {
        return new ParseNode
        {
            Type = ParseNodeType.statements,
            SubNodes = [
                ..CompileZeroOrMore(TryCompileStatement)
            ]
        };
    }

    private ParseNode? TryCompileStatement()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.keyword && (
            currentToken.Value is "let" or "if" or "while" or "do" or "return"
        ))
        {
            return currentToken switch
            {
                { Value: "let" } => CompileLetStatement(),
                { Value: "if" } => CompileIfStatement(),
                { Value: "while" } => CompileWhileStatement(),
                { Value: "do" } => CompileDoStatement(),
                { Value: "return" } => CompileReturnStatement(),
                _ => throw new InvalidOperationException($"invalid value in {nameof(TryCompileStatement)}")
            };
        }
        return null;
    }

    private ParseNode CompileReturnStatement()
    {
        return new ParseNode
        {
            Type = ParseNodeType.returnStatement,
            SubNodes = [
                ConsumeKeyword("return"),
                ..TryCompileExpression(),
                ConsumeSymbol(";")
            ]
        };

        IEnumerable<ParseNode> TryCompileExpression()
        {
            var currentToken = Peek();
            if (currentToken.Type == TokenType.symbol && currentToken.Value == ";")
            {
                return [];
            }
            return [CompileExpression()];
        }
    }

    private ParseNode CompileDoStatement()
    => new()
    {
        Type = ParseNodeType.doStatement,
        SubNodes = [
                ConsumeKeyword("do"),
                ..CompileSubroutineCall(),
                ConsumeSymbol(";")
            ]
    };

    private ParseNode CompileWhileStatement()
    => new()
    {
        Type = ParseNodeType.whileStatement,
        SubNodes = [
                ConsumeKeyword("while"),
                ConsumeSymbol("("),
                CompileExpression(),
                ConsumeSymbol(")"),
                ConsumeSymbol("{"),
                CompileStatements(),
                ConsumeSymbol("}")
            ]
    };

    private ParseNode CompileIfStatement()
    {
        return new ParseNode
        {
            Type = ParseNodeType.ifStatement,
            SubNodes = [
                ConsumeKeyword("if"),
                ConsumeToken(TokenType.symbol, "("),
                CompileExpression(),
                ConsumeToken(TokenType.symbol, ")"),
                ConsumeToken(TokenType.symbol, "{"),
                CompileStatements(),
                ConsumeToken(TokenType.symbol, "}"),
                ..TryCompileElseClause()
            ]
        };

        IEnumerable<ParseNode> TryCompileElseClause()
        {
            var currentToken = Peek();
            if (currentToken.Type == TokenType.keyword && currentToken.Value == "else")
            {
                return [
                    ConsumeKeyword("else"),
                    ConsumeSymbol("{"),
                    CompileStatements(),
                    ConsumeSymbol("}")
                ];
            }
            return [];
        }
    }

    private IEnumerable<ParseNode> CompileZeroOrOne(Func<ParseNode?> compilationRule)
    {
        var compiledNode = compilationRule();
        if (compiledNode is not null)
        {
            return [compiledNode];
        }
        return [];
    }

    private ParseNode CompileLetStatement()
    {
        return new ParseNode
        {
            Type = ParseNodeType.letStatement,
            SubNodes = [
                ConsumeToken(TokenType.keyword, "let"),
                ConsumeExistingIdentifier(false),
                ..TryCompileArrayAccess(),
                ConsumeToken(TokenType.symbol, "="),
                CompileExpression(),
                ConsumeToken(TokenType.symbol, ";")
            ]
        };

        IEnumerable<ParseNode> TryCompileArrayAccess()
            =>
            (Peek() is { Type: TokenType.symbol, Value: "["}) ?
            [
                ConsumeSymbol("["),
                CompileExpression(),
                ConsumeSymbol("]")
            ]
            : [];
    }

    private ParseNode CompileParameterList()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.symbol && currentToken.Value == ")")
        {
            return new ParseNode { Type = ParseNodeType.parameterList };
        }
        var typeNode = ConsumeToken();
        return new ParseNode
        {
            Type = ParseNodeType.parameterList,
            SubNodes = [
                typeNode,
                ConsumeNewIdentifier(IdentifierCategory.Arg, typeNode.Value!.Value),
                ..ConsumeZeroOrMoreParameters()
            ],
            Value = null
        };
    }

    private ParseNode CompileExpression()
    {
        return new ParseNode
        {
            Type = ParseNodeType.expression,
            SubNodes = [
                CompileTerm(),
                ..ConsumeZeroOrMoreOps()
            ]
        };
    }

    private IEnumerable<ParseNode> ConsumeZeroOrMoreOps()
    {
        while (Peek() is { Type: TokenType.symbol, Value: "+" or "-" or "*" or "/" or "&" or "|" or "<" or ">" or "=" })
        {
            yield return ConsumeToken(TokenType.symbol);
            yield return CompileTerm();
        }
    }

    private ParseNode CompileTerm()
    {
        var currentToken = Peek();
        return new ParseNode
        {
            Type = ParseNodeType.term,
            SubNodes = currentToken switch
            {
                { Type: TokenType.intConst } => [ConsumeToken(TokenType.intConst)],
                { Type: TokenType.stringConst } => [ConsumeToken(TokenType.stringConst)],
                { Type: TokenType.keyword } => [ConsumeKeyword()], // 'null' or 'this'
                { Type: TokenType.symbol, Value: "-" or "~" } => [
                    ConsumeSymbol(),
                    CompileTerm()
                    ],
                { Type: TokenType.symbol, Value: "(" } => [
                    ConsumeSymbol("("),
                    CompileExpression(),
                    ConsumeSymbol(")")
                    ],
                { Type: TokenType.identifier } => [
                    ..(PeekNext() switch {
                        { Type: TokenType.symbol, Value: "[" } => CompileArrayAccess(),
                        { Type: TokenType.symbol, Value: "." } => CompileSubroutineCall(),
                        _ => [ConsumeExistingIdentifier(false)]
                    })
                ]
            }
        };

        IEnumerable<ParseNode> CompileArrayAccess()
            =>
            [
                ConsumeExistingIdentifier(false),
                ConsumeSymbol("["),
                CompileExpression(),
                ConsumeSymbol("]")
            ];
    }

    private IEnumerable<ParseNode> CompileSubroutineCall()
    {
        return [
                ..ConsumeSubroutineName(),
                ConsumeSymbol("("),
                CompileExpressionList(),
                ConsumeSymbol(")"),
        ];

        IEnumerable<ParseNode> ConsumeSubroutineName()
        {
            var t = PeekNext();
            // if next token is '.', we are calling a method, otherwise a function
            if (t is { Type: TokenType.symbol, Value: "." })
            {
                yield return ConsumeExistingIdentifier(true);
                yield return ConsumeSymbol(".");
                yield return ConsumeExistingIdentifier(false);
            }
            else
            {
                yield return ConsumeExistingIdentifier(false);
            }
        }

        ParseNode CompileExpressionList()
        {
            return new ParseNode {
                Type = ParseNodeType.expressionList,
                SubNodes = [
                    ..TryCompileExpressionList()
                ]
            };

            IEnumerable<ParseNode> TryCompileExpressionList()
            {
                if (Peek() is { Type: TokenType.symbol, Value: ")" })
                {
                    yield break;
                }
                yield return CompileExpression();
                while (Peek() is not { Type: TokenType.symbol, Value: ")" })
                {
                    yield return ConsumeSymbol(",");
                    yield return CompileExpression();
                }
            }
        }
    }

    private Token Peek()
        => Tokens[CurrentIndex];

    private Token PeekNext()
        => Tokens[CurrentIndex + 1];

    private ParseNode ConsumeToken(TokenType? expectedType = null, string? expectedValue = null)
    {
        var t = Tokens[CurrentIndex++];
        if ((expectedType is not null && t.Type != expectedType) || (expectedValue is not null && t.Value != expectedValue))
        {
            throw new InvalidDataException();
        }
        return new() { Type = TypeDict[t.Type], Value = t };
    }

    private Identifier ConsumeExistingIdentifier(bool isClass)
    {
        var t = ConsumeToken(TokenType.identifier);
        var name = t.Value!.Value;
        var i = -1;

        var kind = SubroutineLevelTable?.KindOf(name) ?? SymbolKind.None;
        var varType = SubroutineLevelTable?.TypeOf(name) ?? string.Empty;
        if (kind == SymbolKind.None)
        {
            kind = ClassLevelTable.KindOf(name);
        }
        if (varType == string.Empty)
        {
            varType = ClassLevelTable.TypeOf(name);
        }

        if (kind is not SymbolKind.None)
        {
            i = SubroutineLevelTable?.IndexOf(name) ?? -1;
            if (i == -1)
            {
                i = ClassLevelTable.IndexOf(name);
            }
        }

        var category = kind switch {
            SymbolKind.Static => IdentifierCategory.Static,
            SymbolKind.Field => IdentifierCategory.Field,
            SymbolKind.Var => IdentifierCategory.Var,
            SymbolKind.Arg => IdentifierCategory.Arg,
            SymbolKind.None => isClass ? IdentifierCategory.Class : IdentifierCategory.Subroutine,
            _ => throw new InvalidOperationException("should never happen; illegal SymbolKind")
        };

        return new Identifier {
            Type = ParseNodeType.identifier,
            Value = t.Value,
            Name = t.Value!.Value,
            VarType = varType,
            Category = category,
            Declaration = false,
            Index = i
        };
    }

    private Identifier ConsumeNewIdentifier(IdentifierCategory category, string varType)
    {
        var t = ConsumeToken(TokenType.identifier);
        var name = t.Value!.Value;
        var i = -1;
        if (category is IdentifierCategory.Class)
        {
            varType = name;
        }

        // add to symbol table
        if (category is IdentifierCategory.Field or IdentifierCategory.Static)
        {
            i = ClassLevelTable.Define(name, varType, category is IdentifierCategory.Static ? SymbolKind.Static : SymbolKind.Field);
        }
        else if (category is IdentifierCategory.Arg or IdentifierCategory.Var)
        {
            i = SubroutineLevelTable!.Define(name, varType, category is IdentifierCategory.Var ? SymbolKind.Var : SymbolKind.Arg);
        }

        return new Identifier {
            Type = ParseNodeType.identifier,
            Value = t.Value,
            Name = t.Value!.Value,
            VarType = varType,
            Category = category,
            Declaration = true,
            Index = i
        };
    }

    private ParseNode ConsumeKeyword(string? expectedValue = null)
        => ConsumeToken(TokenType.keyword, expectedValue);

    private ParseNode ConsumeSymbol(string? expectedValue = null)
        => ConsumeToken(TokenType.symbol, expectedValue);
}

public record Identifier : ParseNode
{
    required public string Name { get; init; }
    required public IdentifierCategory Category { get; init; }
    required public string VarType { get; init; }
    required public int Index { get; init; }
    required public bool Declaration { get; init; } // if not declaration, than usage
}

public enum IdentifierCategory
{
    Field,
    Static,
    Var,
    Arg,
    Class,
    Subroutine
}

public record ParseNode
{
    public required ParseNodeType Type { get; init; }
    public List<ParseNode> SubNodes { get; init; } = [];
    public Token? Value { get; set; }
}

public enum ParseNodeType
{
    @class,
    classVarDec,
    subroutineDec,
    parameterList,
    subroutineBody,
    varDec,
    statements,
    letStatement,
    ifStatement,
    whileStatement,
    doStatement,
    returnStatement,
    expression,
    term,
    expressionList,
    keyword,
    symbol,
    intConst,
    stringConst,
    identifier
}
