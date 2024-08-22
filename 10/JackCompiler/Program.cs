using System.Xml.Linq;

namespace JackCompiler;

public static class Program
{
    public static int Main(string[] args)
    {
        var path = args[0];
        var fileAttr = File.GetAttributes(path);

        if (fileAttr.HasFlag(FileAttributes.Directory))
        {
            foreach (var f in Directory.EnumerateFiles(path, "*.jack"))
            {
                Parse(f);
            }
        }
        else
        {
            Parse(path);
        }

        return 0;
    }

    private static void Parse(string path)
    {
        var file = File.ReadAllText(path);
        var tokenizer = new JackTokenizer { OriginalFile = file };
        tokenizer.ProcessFile();
        var tokens = tokenizer.Tokens;
        var parseTree = Compile(tokens);
        WriteFile(path, parseTree);
    }

    private static ParseNode Compile(List<Token> tokens)
    {
        var compiler = new CompilationEngine { Tokens = tokens };
        compiler.Compile();
        return compiler.RootNode!;
    }

    private static void WriteFile(string path, ParseNode parseTree)
    {
        var doc = new XDocument(
            parseTree.ToXml()
        );
        doc.Save(path.Replace(".jack", "Parsed.xml"));
    }

    public static XElement ToXml(this ParseNode parseNode)
    {
        if (parseNode.Value is not null)
        {
            return new XElement(parseNode.Type.ToString(), parseNode.Value.Value);
        }

        var subElements = parseNode.SubNodes.Select(ToXml);
        return new XElement(parseNode.Type.ToString("G"), subElements);
    }
}

public record Token
{
    public TokenType Type { get; init; }
    required public string Value { get; init; }
}

public enum TokenType
{
    None,
    keyword,
    identifier,
    symbol,
    intConst,
    stringConst
}

public enum Keyword
{
    Class,
    Method,
    Function,
    Constructor,
    Int,
    Boolean,
    Char,
    Void,
    Var,
    Static,
    Field,
    Let,
    Do,
    If,
    Else,
    While,
    Return,
    True,
    False,
    Null,
    This
}
