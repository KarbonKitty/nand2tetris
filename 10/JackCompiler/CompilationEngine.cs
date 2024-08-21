namespace JackCompiler;

public class CompilationEngine
{
    public required List<Token> Tokens { get; init; }

    public ParseNode? RootNode { get; set; }

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
        RootNode = CompileClass(); // single class per file ?
        // while (CurrentIndex < Tokens.Count)
        // {
        //     var node = Tokens[CurrentIndex] switch {
        //         { Type: TokenType.Keyword, Value: "class" } => CompileClass(),
        //         _ => throw new InvalidOperationException("code only legal in class")
        //     };
        // }

    }

    private ParseNode CompileClass()
        => new ParseNode
        {
            Type = ParseNodeType.@class,
            SubNodes = [
                    ConsumeToken(TokenType.keyword, "class"),
                    ConsumeToken(TokenType.identifier),
                    ConsumeToken(TokenType.symbol, "{"),
                    ..CompileZeroOrMore(TryCompileClassVarDec),
                    ..CompileZeroOrMore(TryCompileSubroutineDec),
                    ConsumeToken(TokenType.symbol, "}"),
                ],
            Value = null
        };

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
        return new ParseNode {
            Type = ParseNodeType.classVarDec,
            SubNodes = [
                ConsumeToken(TokenType.keyword), // it can be either 'static' or 'field'
                ConsumeToken(), // it can be whatever type we allow,
                ConsumeToken(TokenType.identifier), // var name
                ..ConsumeZeroOrMoreVarNames(),
                ConsumeToken(TokenType.symbol, ";")
            ],
            Value = null
        };
    }

    private IEnumerable<ParseNode> ConsumeZeroOrMoreParameters()
    {
        var currentToken = Peek();
        while ((currentToken.Type == TokenType.symbol) && (currentToken.Value == ","))
        {
            yield return ConsumeSymbol(",");
            yield return ConsumeToken(); // type
            yield return ConsumeIdentifier();
            currentToken = Peek();
        }
    }

    private IEnumerable<ParseNode> ConsumeZeroOrMoreVarNames()
    {
        var currentToken = Peek();
        while ((currentToken.Type == TokenType.symbol) && (currentToken.Value == ","))
        {
            yield return ConsumeToken(TokenType.symbol, ",");
            yield return ConsumeToken(TokenType.identifier);
            currentToken = Peek();
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
        return new ParseNode {
            Type = ParseNodeType.subroutineDec,
            SubNodes = [
                ConsumeToken(TokenType.keyword),
                ConsumeToken(), // void or type
                ConsumeToken(), // identifier
                ConsumeToken(TokenType.symbol, "("),
                CompileParameterList(),
                ConsumeToken(TokenType.symbol, ")"),
                CompileSubroutineBody()
            ]
        };
    }

    private ParseNode CompileSubroutineBody()
    {
        return new ParseNode {
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
        return new ParseNode {
            Type = ParseNodeType.varDec,
            SubNodes = [
                ConsumeKeyword("var"),
                ConsumeToken(), // type
                ConsumeIdentifier(),
                ..ConsumeZeroOrMoreVarNames(),
                ConsumeSymbol(";")
            ]
        };
    }

    private ParseNode CompileStatements()
    {
        return new ParseNode {
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
            return currentToken switch {
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
        return new ParseNode {
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
    {
        return new ParseNode {
            Type = ParseNodeType.doStatement,
            SubNodes = [
                ConsumeKeyword("do"),
                // subroutine call
                ..ConsumeSubroutineName(),
                ConsumeSymbol("("),
                ..TryCompileExpressions(),
                ConsumeSymbol(")"),
                // end subroutine call
                ConsumeSymbol(";")
            ]
        };

        IEnumerable<ParseNode> ConsumeSubroutineName()
        {
            yield return ConsumeIdentifier();
            // if next token is '.', we are calling a method, otherwise a function
            var currentToken = Peek();
            if (currentToken.Type == TokenType.symbol && currentToken.Value == ".")
            {
                yield return ConsumeSymbol(".");
                yield return ConsumeIdentifier();
            }
        }

        IEnumerable<ParseNode> TryCompileExpressions()
        {
            var currentToken = Peek();
            while (!(currentToken.Type == TokenType.symbol && currentToken.Value == ")"))
            {
                yield return CompileExpression();
                currentToken = Peek();
            }
        }
    }

    private ParseNode CompileWhileStatement()
    {
        return new ParseNode {
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
    }

    private ParseNode CompileIfStatement()
    {
        return new ParseNode {
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
        return new ParseNode {
            Type = ParseNodeType.letStatement,
            SubNodes = [
                ConsumeToken(TokenType.keyword, "let"),
                ConsumeToken(TokenType.identifier), // var name
                // TODO: array-oriented expression
                ConsumeToken(TokenType.symbol, "="),
                CompileExpression(),
                ConsumeToken(TokenType.symbol, ";")
            ]
        };
    }

    private ParseNode CompileParameterList()
    {
        var currentToken = Peek();
        if (currentToken.Type == TokenType.symbol && currentToken.Value == ")")
        {
            return new ParseNode { Type = ParseNodeType.parameterList };
        }
        return new ParseNode {
            Type = ParseNodeType.parameterList,
            SubNodes = [
                ConsumeToken(), // type
                ConsumeToken(TokenType.identifier), // identifier
                ..ConsumeZeroOrMoreParameters()
            ],
            Value = null
        };
    }

    private Token Peek()
        => Tokens[CurrentIndex];

    private ParseNode ConsumeToken(TokenType? expectedType = null, string? expectedValue = null)
    {
        var t = Tokens[CurrentIndex++];
        if ((expectedType is not null && t.Type != expectedType) || (expectedValue is not null && t.Value != expectedValue))
        {
            throw new InvalidDataException();
        }
        return new() { Type = TypeDict[t.Type], Value = t };
    }

    // TODO: replace with actual expression compilation
    private ParseNode CompileExpression()
    {
        return new ParseNode {
            Type = ParseNodeType.expression,
            SubNodes = [
                ConsumeToken() // it is sometimes 'this', which is a keyword, not identifier
            ]
        };
    }

    private ParseNode ConsumeKeyword(string? expectedValue = null)
        => ConsumeToken(TokenType.keyword, expectedValue);

    private ParseNode ConsumeSymbol(string? expectedValue = null)
        => ConsumeToken(TokenType.symbol, expectedValue);

    private ParseNode ConsumeIdentifier()
        => ConsumeToken(TokenType.identifier);
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
