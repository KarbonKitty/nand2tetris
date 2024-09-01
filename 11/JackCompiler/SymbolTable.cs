namespace JackCompiler;

public class SymbolTable
{
    private List<Symbol> Symbols { get; } = [];

    public int Define(string name, string type, SymbolKind kind)
    {
        var index = VarCount(kind);
        Symbols.Add(new Symbol { Name = name, Type = type, Kind = kind, Index = index });
        return index;
    }

    public int VarCount(SymbolKind kind)
        => Symbols.Count(s => s.Kind == kind);

    public SymbolKind KindOf(string name)
        => Symbols.FirstOrDefault(s => s.Name == name)?.Kind ?? SymbolKind.None;

    public string TypeOf(string name)
        => Symbols.FirstOrDefault(s => s.Name == name)?.Type ?? string.Empty;

    public int IndexOf(string name)
        => Symbols.FirstOrDefault(s => s.Name == name)?.Index ?? -1;
}

public record Symbol
{
    required public string Name { get; init; }
    required public string Type { get; init; }
    required public SymbolKind Kind { get; init; }
    required public int Index { get; init; }
}

public enum SymbolKind
{
    Static,
    Field,
    Var,
    Arg,
    None
}
