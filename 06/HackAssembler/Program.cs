using System.Text;

public class Program
{
    public static int Main(string[] args)
    {
        var path = args[0];
        try
        {
            var fileContent = File.ReadAllText(path);
            var assembler = new Assembler(fileContent);
            Console.Write(assembler.ProcessFile());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Can't open file at path '{path}': {ex}");
        }

        return 0;
    }
}

public class Assembler
{
    private readonly string[] _originalFile;
    private Dictionary<string, ushort> SymbolTable { get; } = [];
    private StringBuilder _outputFileBuilder = new();

    private static readonly Dictionary<string, ushort> CompDict = new()
    {
        { "0", 0b0101010 },
        { "1", 0b0111111 },
        { "-1", 0b0111010 },
        { "D", 0b0001100 },
        { "A", 0b0110000 },
        { "M", 0b1110000 },
        { "!D", 0b0001101 },
        { "!A", 0b0110001 },
        { "!M", 0b1110001 },
        { "-D", 0b0001111 },
        { "-A", 0b0110011 },
        { "-M", 0b1110011 },
        { "D+1", 0b0011111 },
        { "A+1", 0b0110111 },
        { "M+1", 0b1110111 },
        { "D-1", 0b0001110 },
        { "A-1", 0b0110010 },
        { "M-1", 0b1110010 },
        { "D+A", 0b0000010 },
        { "D+M", 0b1000010 },
        { "D-A", 0b0010011 },
        { "D-M", 0b1010011 },
        { "A-D", 0b0000111 },
        { "M-D", 0b1000111 },
        { "D&A", 0b0000000 },
        { "D&M", 0b1000000 },
        { "D|A", 0b0010101 },
        { "D|M", 0b1010101 }
    };

    private static readonly Dictionary<string, ushort> DestDict = new() {
        { "null", 0b000 },
        { "M", 0b001 },
        { "D", 0b010 },
        { "MD", 0b011 },
        { "A", 0b100 },
        { "AM", 0b101 },
        { "AD", 0b110 },
        { "ADM", 0b111 }
    };

    private static readonly Dictionary<string, ushort> JumpDict = new() {
        { "null", 0b000 },
        { "JGT", 0b001 },
        { "JEQ", 0b010 },
        { "JGE", 0b011 },
        { "JLT", 0b100 },
        { "JNE", 0b101 },
        { "JLE", 0b110 },
        { "JMP", 0b111 }
    };

    public Assembler(string fileContent)
    {
        _originalFile = fileContent.Split("\n");
        InitializeSymbolsTable();
    }

    public string ProcessFile()
    {
        BuildSymbolTable();
        Assemble();
        return _outputFileBuilder.ToString().TrimEnd();
    }

    private void InitializeSymbolsTable()
    {
        SymbolTable.Add("R0", 0);
        SymbolTable.Add("R1", 1);
        SymbolTable.Add("R2", 2);
        SymbolTable.Add("R3", 3);
        SymbolTable.Add("R4", 4);
        SymbolTable.Add("R5", 5);
        SymbolTable.Add("R6", 6);
        SymbolTable.Add("R7", 7);
        SymbolTable.Add("R8", 8);
        SymbolTable.Add("R9", 9);
        SymbolTable.Add("R10", 10);
        SymbolTable.Add("R11", 11);
        SymbolTable.Add("R12", 12);
        SymbolTable.Add("R13", 13);
        SymbolTable.Add("R14", 14);
        SymbolTable.Add("R15", 15);
        SymbolTable.Add("SP", 0);
        SymbolTable.Add("LCL", 1);
        SymbolTable.Add("ARG", 2);
        SymbolTable.Add("THIS", 3);
        SymbolTable.Add("THAT", 4);
        SymbolTable.Add("SCREEN", 16384);
        SymbolTable.Add("KBD", 24576);
    }

    private void BuildSymbolTable()
    {
        ushort lineNumber = 0;
        foreach (var line in _originalFile)
        {
            var trimmedLine = line.Trim();
            if (IsInstruction(trimmedLine))
            {
                lineNumber++;
            }
            var (isLabel, label) = ExtractLabel(trimmedLine);
            if (isLabel)
            {
                SymbolTable.Add(label, lineNumber);
            }
        }
    }

    private void Assemble()
    {
        ushort variableAddress = 16;
        foreach (var line in _originalFile)
        {
            var trimmedLine = line.Trim();
            ushort binaryInstruction;
            if (IsAInstruction(trimmedLine))
            {
                (binaryInstruction, var bumpVarAddress)
                    = ProcessAInstruction(trimmedLine.TrimStart('@'), variableAddress);
                if (bumpVarAddress)
                {
                    variableAddress++;
                }
            }
            else if (IsCInstruction(trimmedLine))
            {
                binaryInstruction = ProcessCInstruction(trimmedLine);
            }
            else
            {
                continue;
            }
            _outputFileBuilder.AppendLine(binaryInstruction.ToString("b16"));
        }
    }

    private (ushort, bool) ProcessAInstruction(string a, ushort variableAddress)
    {
        var isANumeric = ushort.TryParse(a, out var result);
        if (isANumeric)
        {
            return (result, false);
        }

        if (SymbolTable.TryGetValue(a, out ushort value))
        {
            return (value, false);
        }

        SymbolTable[a] = variableAddress;

        return (SymbolTable[a], true);
    }

    private ushort ProcessCInstruction(string line)
    {
        string? comp, dest, jump, rest;
        if (line.Contains('=') && line.Contains(';'))
        {
            dest = line.Split('=')[0];
            rest = line.Split('=')[1];
            comp = rest.Split(';')[0];
            jump = rest.Split(';')[1];
        }
        else if (line.Contains('='))
        {
            dest = line.Split('=')[0];
            comp = line.Split('=')[1];
            jump = null;
        }
        else if (line.Contains(';'))
        {
            comp = line.Split(';')[0];
            jump = line.Split(';')[1];
            dest = null;
        }
        else
        {
            comp = line;
            dest = null;
            jump = null;
        }
        return (ushort)(0b111 << 13 | CompDict[comp] << 6 | DestDict[dest ?? "null"] << 3 | JumpDict[jump ?? "null"]);
    }

    private bool IsAInstruction(string line)
        => line.StartsWith('@');

    private bool IsCInstruction(string line)
        => !IsLabel(line) && !IsAInstruction(line) && !IsComment(line) && !string.IsNullOrWhiteSpace(line);

    private bool IsInstruction(string line)
        => IsAInstruction(line) || IsCInstruction(line);

    private bool IsLabel(string line)
        => line.StartsWith('(');

    private bool IsComment(string line)
        => line.StartsWith("//");

    private (bool, string) ExtractLabel(string line)
    {
        if (line.StartsWith('(') && line.EndsWith(')'))
        {
            return (true, line.TrimStart('(').TrimEnd(')'));
        }
        return (false, string.Empty);
    }
}
