using System.Text;

namespace JackCompiler;

public class JackTokenizer
{
    public static readonly string[] Keywords = [ "class", "method", "function", "function", "constructor", "int", "boolean", "char", "void", "var", "static", "field", "let", "do", "if", "else", "while", "return", "true", "false", "null", "this" ];

    public required string OriginalFile { get; init; }

    int CurrentIndex { get; set; }

    public List<Token> Tokens { get; set; } = [];

    public void ProcessFile()
    {
        CurrentIndex = 0;

        while (CurrentIndex < OriginalFile.Length)
        {
            var currentCharacter = OriginalFile[CurrentIndex];
            var result = currentCharacter switch
            {
                '"' => HandleDoubleQuote(),
                '\r' or '\n' or ' ' or '\t' => Advance(),
                '{' or '}' or '[' or ']' or '(' or ')' or '.' or ',' or ';' or '+' or '-' or '*' or '/' or '&' or '|' or '<' or '>' or '=' or '~' => HandleSymbol(currentCharacter),
                '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' => HandleDigit(currentCharacter),
                _ => HandleOtherChar()
            };
            if (result is not null)
            {
                Tokens.Add(result);
            }
        }
    }

    private Token? HandleDoubleQuote()
    {
        var startIndex = CurrentIndex;
        var lastChar = Consume();
        var c = Consume();
        while (c != '"' || (c == '"' && lastChar == '\\'))
        {
            lastChar = c;
            c = Consume();
        }
        return new Token { Type = TokenType.stringConst, Value = OriginalFile[(startIndex + 1)..(CurrentIndex - 1)] };
    }

    private Token? Advance()
    {
        CurrentIndex++;
        return null;
    }

    private Token? HandleSymbol(char character)
    {
        if (character == '/' && (PeekNext() == '/' || PeekNext() == '*'))
        {
            return HandleComment();
        }
        var c = Consume();
        return new() { Type = TokenType.symbol, Value = c.ToString() };
    }

    private Token? HandleComment()
    {
        Consume();
        var c = Consume();
        if (c == '/')
        {
            while (c != '\n')
            {
                c = Consume();
            }
            return null;
        }
        else
        {
            var lastChar = '\0';
            while (c != '/' || (lastChar != '*'))
            {
                lastChar = c;
                c = Consume();
            }
            return null;
        }
    }

    private Token? HandleDigit(char character)
    {
        var startIndex = CurrentIndex;
        var c = Consume();
        while (char.IsDigit(PeekCurrent()))
        {
            c = Consume();
        }
        return new Token { Type = TokenType.intConst, Value = OriginalFile[startIndex..CurrentIndex] };
    }

    private Token? HandleOtherChar()
    {
        var startIndex = CurrentIndex;
        var c = Consume();
        while (char.IsAsciiLetterOrDigit(PeekCurrent()) || PeekCurrent() == '_')
        {
            c = Consume();
        }
        var s = OriginalFile[startIndex .. CurrentIndex];
        var type = Keywords.Contains(s) ? TokenType.keyword : TokenType.identifier;
        return new Token { Type = type, Value = s };
    }

    private char PeekNext()
        => OriginalFile[CurrentIndex + 1];

    private char PeekCurrent()
        => OriginalFile[CurrentIndex];

    private char Consume()
        => OriginalFile[CurrentIndex++];
}
