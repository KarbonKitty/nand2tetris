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
        var vmWriter = new VmWriter(parseTree);
        vmWriter.WriteVmCode();
        WriteFile(path, vmWriter.VmCode);
    }

    private static ParseNode Compile(List<Token> tokens)
    {
        var compiler = new CompilationEngine { Tokens = tokens };
        compiler.Compile();
        return compiler.RootNode!;
    }

    private static void WriteFile(string path, List<string> vmCode)
    {
        var text = string.Join(Environment.NewLine, vmCode);
        File.WriteAllText(path.Replace(".jack", "Parsed.vm"), text);
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
