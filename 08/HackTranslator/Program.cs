using System.Text;

namespace HackTranslator;

public class Program
{
    public static int Main(string[] args)
    {
        var folderPath = args[0];
        Console.Write(Translator.BootstrappingCode());
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.vm"))
        {
            try
            {
                var fileContent = File.ReadAllLines(path);
                var fileName = Path.GetFileNameWithoutExtension(path);
                var translator = new Translator(fileName, fileContent);
                Console.Write(translator.ProcessFile());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Can't open file at path '{path}': {ex}");
            }
        }

        return 0;
    }
}

public class Translator
{
    private readonly string[] _originalFile;
    private readonly string _fileName;

    public Translator(string fileName, string[] fileContent)
    {
        _originalFile = fileContent;
        _fileName = fileName;
    }

    public static string BootstrappingCode()
    {
        var outputBuilder = new StringBuilder();

        // set up stack pointer
        outputBuilder.AppendLine("// initialize stack pointer");
        outputBuilder.AppendLine("@256");
        outputBuilder.AppendLine("D=A");
        outputBuilder.AppendLine("@SP");
        outputBuilder.AppendLine("M=D");

        // call the Sys.init
        var initCodeWriter = new CodeWriter("Sys");
        var code = initCodeWriter.WriteCode(new Command { 
            Type = CommandType.Call,
            FirstArgument = "Sys.init",
            SecondArgument = 0
        });
        foreach (var line in code)
        {
            outputBuilder.AppendLine(line);
        }

        return outputBuilder.ToString();
    }

    public string ProcessFile()
    {
        var outputBuilder = new StringBuilder();

        var codeWriter = new CodeWriter(_fileName);

        foreach (var vmCommand in _originalFile)
        {
            var command = Parser.Parse(vmCommand);
            var outputCode = codeWriter.WriteCode(command);
            foreach (var hackCommand in outputCode)
            {
                outputBuilder.AppendLine(hackCommand);
            }
        }

        return outputBuilder.ToString();
    }
}

public static class Parser
{
    public static Command Parse(string vmCommand)
    {
        // process comments
        vmCommand = vmCommand.Split("//").First();

        if (string.IsNullOrWhiteSpace(vmCommand))
        {
            return new() {
                Type = CommandType.EmptyLine,
                FirstArgument = null,
                SecondArgument = null
            };
        }
        // else if (vmCommand.StartsWith("//"))
        // {
        //     return new() {
        //         Type = CommandType.Comment,
        //         FirstArgument = null,
        //         SecondArgument = null
        //     };
        // }

        var parts = vmCommand.Trim().Split(' ');
        return parts[0] switch {
            "push" => new() { Type = CommandType.Push, FirstArgument = parts[1], SecondArgument = int.Parse(parts[2]) },
            "pop" => new() { Type = CommandType.Pop, FirstArgument = parts[1], SecondArgument = int.Parse(parts[2]) },
            "add" => new() { Type = CommandType.Arithmetic, FirstArgument = "add", SecondArgument = null },
            "sub" => new() { Type = CommandType.Arithmetic, FirstArgument = "sub", SecondArgument = null },
            "neg" => new() { Type = CommandType.Arithmetic, FirstArgument = "neg", SecondArgument = null },
            "eq" => new() { Type = CommandType.Arithmetic, FirstArgument = "eq", SecondArgument = null },
            "gt" => new() { Type = CommandType.Arithmetic, FirstArgument = "gt", SecondArgument = null },
            "lt" => new() { Type = CommandType.Arithmetic, FirstArgument = "lt", SecondArgument = null },
            "and" => new() { Type = CommandType.Arithmetic, FirstArgument = "and", SecondArgument = null },
            "or" => new() { Type = CommandType.Arithmetic, FirstArgument = "or", SecondArgument = null },
            "not" => new() { Type = CommandType.Arithmetic, FirstArgument = "not", SecondArgument = null },
            "label" => new() { Type = CommandType.Label, FirstArgument = parts[1], SecondArgument = null },
            "goto" => new() { Type = CommandType.Goto, FirstArgument = parts[1], SecondArgument = null },
            "if-goto" => new() { Type = CommandType.If, FirstArgument = parts[1], SecondArgument = null },
            "function" => new() { Type = CommandType.Function, FirstArgument = parts[1], SecondArgument = int.Parse(parts[2]) },
            "call" => new() { Type = CommandType.Call, FirstArgument = parts[1], SecondArgument = int.Parse(parts[2]) },
            "return" => new() { Type = CommandType.Return, FirstArgument = "return", SecondArgument = null },
            _ => new() { Type = CommandType.Comment, FirstArgument = null, SecondArgument = null }
        };
    }
}

public class CodeWriter
{
    private static int labelCount = 0;
    private static int callCount = 0;
    private string? FunctionName { get; set; }
    private string FileName { get; set; }

    public CodeWriter(string fileName)
        => FileName = fileName;

    public string[] WriteCode(Command command)
    {
        return command switch {
            { Type: CommandType.Arithmetic } => WriteALU(command.FirstArgument!),
            { Type: CommandType.Push } => WritePush(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Pop } => WritePop(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Label } => WriteLabel(command.FirstArgument!),
            { Type: CommandType.Goto } => WriteGoto(command.FirstArgument!),
            { Type: CommandType.If } => WriteIf(command.FirstArgument!),
            { Type: CommandType.Function } => WriteFunction(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Call } => WriteCall(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Return } => WriteReturn(command.FirstArgument!),
            { Type: CommandType.Comment } => [],
            _ => []
        };
    }

    private string[] WriteFunction(string functionName, int numberOfLocals)
    {
        FunctionName = functionName;

        string[] pushLocal() => ["@0", "D=A", ..PushOntoStack()];

        var locals = new List<string>();

        for (var i = 0; i < numberOfLocals; i++)
        {
            locals.AddRange(pushLocal());
        }

        string[] assembly = [
            $"// function {functionName} {numberOfLocals}",
            $"({functionName})",
            ..locals
        ];

        return assembly;
    }

    private string[] WriteCall(string functionName, int numberOfArguments)
    {
        callCount++;
        var returnAddressLabel = $"{functionName}$ret.{callCount}";

        string [] assembly = [
            $"// call {functionName}",
            $"@{returnAddressLabel}", "D=A", ..PushOntoStack(),
            // push LCL
            "@LCL", "D=M", ..PushOntoStack(),
            // push ARG
            "@ARG", "D=M", ..PushOntoStack(),
            // push THIS
            "@THIS", "D=M", ..PushOntoStack(),
            // push THAT
            "@THAT", "D=M", ..PushOntoStack(),
            // reposition ARG
            "@SP", "D=M", "@5", "D=D-A", $"@{numberOfArguments}", "D=D-A", "@ARG", "M=D",
            // reposition LCL
            "@SP", "D=M", "@LCL", "M=D",
            $"@{functionName}", "0;JMP",
            $"({returnAddressLabel})"
        ];

        return assembly;
    }

    private string[] WriteReturn(string command)
    {
        string[] assembly = [
            "// return",
            // frame pointer is stored in R13
            "@LCL", "D=M", "@R13", "M=D",
            // return address is stored in R14
            "@5", "A=D-A", "D=M", "@R14", "M=D",
            // reposition the return value
            ..PopFromStack(), "@ARG", "A=M", "M=D",
            // reposition stack pointer
            "@ARG", "D=M+1", "@SP", "M=D",
            // restore LCL
            "@R13", "D=M", "@4", "A=D-A", "D=M", "@LCL", "M=D",
            // restore ARG
            "@R13", "D=M", "@3", "A=D-A", "D=M", "@ARG", "M=D",
            // restore THIS
            "@R13", "D=M", "@2", "A=D-A", "D=M", "@THIS", "M=D",
            // restore THAT
            "@R13", "D=M", "@1", "A=D-A", "D=M", "@THAT", "M=D",
            // return
            "@R14", "A=M", "0;JMP"
        ];

        return assembly;
    }

    private string[] WriteALU(string command)
    => command switch {
        "add" => [ $"// add", ..PopFromStack(), "@SP", "A=M-1", "M=D+M" ],
        "sub" => [ $"// sub", ..PopFromStack(), "@SP", "A=M-1", "M=M-D" ],
        "neg" => [ $"// neg", ..PopFromStack(), "D=-D", ..PushOntoStack() ],
        "eq" => WriteEq(),
        "gt" => WriteGt(),
        "lt" => WriteLt(),
        "and" => [ $"// and", ..PopFromStack(), "@SP", "A=M-1", "M=D&M" ],
        "or" => [ $"// or", ..PopFromStack(), "@SP", "A=M-1", "M=D|M" ],
        "not" => [ $"// not", ..PopFromStack(), "D=!D", ..PushOntoStack() ],
        _ => throw new InvalidOperationException("Illegal ALU command")
    };

    private string[] WriteLabel(string label)
        => [ $"// label {label}", $"({FunctionName}${label})" ];

    private string[] WriteGoto(string label)
        => [ $"// goto {label}", $"@{FunctionName}${label}", "0;JMP" ];

    private string[] WriteIf(string label)
    {
        var jumpLabel = PutLabel("IFEND");
        return [$"// if-goto {label}", ..PopFromStack(), $"@{jumpLabel}", "D;JEQ", $"@{FunctionName}${label}", "0;JMP", $"({jumpLabel})" ];
    }

    private string PutLabel(string labelBase)
    {
        labelCount++;
        return $"{FileName}.{labelBase}.{labelCount}";
    }

    private string[] WritePush(string segment, int index)
    => segment switch
    {
        Segment.Constant => [ $"// push constant {index}", $"@{index}", "D=A", ..PushOntoStack() ],
        Segment.Static => [ $"// push static {index}", ..ReadStaticVariable($"{FileName}.{index}"), ..PushOntoStack() ],
        Segment.Temp => [ $"// push temp {index}", $"@{index+5}", "D=M", ..PushOntoStack() ],
        Segment.Pointer => [ $"// push pointer {index}", $"@R{index+3}", "D=M", ..PushOntoStack() ],
        Segment.Local or Segment.Argument or Segment.This or Segment.That => [ $"// push {segment} {index}", ..PullFromNormalSegment(segment, index), ..PushOntoStack() ],
        _ => throw new InvalidOperationException($"Illegal segment of push command: {segment}")
    };

    private string[] WritePop(string segment, int index)
    => segment switch {
        Segment.Static => [ $"// pop static {index}", ..PopFromStack(), ..StoreStaticVariable($"{FileName}.{index}") ],
        Segment.Temp => [ $"// pop temp {index}", ..PopFromStack(), ..PutIntoTemp(index) ],
        Segment.Pointer => [ $"// pop pointer {index}", ..PopFromStack(), $"@R{index+3}", "M=D" ],
        Segment.Local or Segment.Argument or Segment.This or Segment.That => [ $"// pop {segment} {index}", ..PopFromStack(), ..PutIntoNormalSegment(segment, index) ],
        _ => throw new InvalidOperationException($"Illegal segment of pop command: {segment}")
    };

    private string[] WriteEq()
    {
        var endOfEqualityCheckLabel = PutLabel("EQEND");
        return [
            "// eq",
            ..PopFromStack(),
            // calculate the difference on stack
            "@SP", "AM=M-1", "D=M-D",
            // push zero into R13
            "@R13", "M=0",
            // if D!=0, skip to pushing to stack
            $"@{endOfEqualityCheckLabel}", "D;JNE",
            // push -1 into R13
            "@R13", "M=-1",
            // label to skip to
            $"({endOfEqualityCheckLabel})",
            // retrieve correct value from R13 into D
            "@R13", "D=M",
            ..PushOntoStack()
        ];
    }

    private string[] WriteGt()
    {
        var endOfEqualityCheckLabel = PutLabel("GTEND");
        return [
            "// gt",
            ..PopFromStack(),
            // calculate the difference on stack
            "@SP", "AM=M-1", "D=M-D",
            // push zero into R13
            "@R13", "M=0",
            // if D<0, skip to pushing to stack
            $"@{endOfEqualityCheckLabel}", "D;JLE",
            // push -1 into R13
            "@R13", "M=-1",
            // label to skip to
            $"({endOfEqualityCheckLabel})",
            // retrieve correct value from R13 into D
            "@R13", "D=M",
            ..PushOntoStack()
        ];
    }

    private string[] WriteLt()
    {
        var endOfEqualityCheckLabel = PutLabel("LTEND");
        return [
            "// lt",
            ..PopFromStack(),
            // calculate the difference on stack
            "@SP", "AM=M-1", "D=M-D",
            // push zero into R13
            "@R13", "M=0",
            // if D>0, skip to pushing to stack
            $"@{endOfEqualityCheckLabel}", "D;JGE",
            // push -1 into R13
            "@R13", "M=-1",
            // label to skip to
            $"({endOfEqualityCheckLabel})",
            // retrieve correct value from R13 into D
            "@R13", "D=M",
            ..PushOntoStack()
        ];
    }

    private static string[] PopFromStack()
        => [ /* --SP */ "@SP", "M=M-1", /* get address from stack pointer */ "A=M", "D=M", /* now we have popped last value from the stack into D register */ ];

    private static string[] PushOntoStack()
        => [ /* get address from SP */ "@SP", "A=M", /* save data to address */ "M=D", /* SP++ */ "@SP", "M=M+1" ];

    private static string[] PutIntoTemp(int index)
        => [ $"@{index+5}", "M=D" ];

    private static string[] StoreStaticVariable(string name)
        => [$"@{name}", "M=D"];

    private static string[] ReadStaticVariable(string name)
        => [ $"@{name}", "D=M" ];

    // we need to pull the base address from a known place in the RAM
    // and offset it by the index
    private static string[] PutIntoNormalSegment(string segment, int index)
        =>
        [
            // store D in R14
            "@R14", "M=D",
            // calculate target address
            $"@{Segment.Shorthand[segment]}", "D=M", $"@{index}", "D=D+A",
            // store target addres in R15
            "@R15", "M=D",
            // get data from R14 into D
            "@R14", "D=M",
            // retrieve the address
            "@R15", "A=M",
            // store the data
            "M=D"
        ];

    private static string[] PullFromNormalSegment(string segment, int index)
        => [ $"@{Segment.Shorthand[segment]}", "D=M", $"@{index}", "A=D+A", "D=M" ];
}

public record Command
{
    public required CommandType Type { get; init; }
    public required string? FirstArgument { get; init; }
    public required int? SecondArgument { get; init; }
}

public static class Segment
{
    public const string Argument = "argument";
    public const string Local = "local";
    public const string Static = "static";
    public const string Constant = "constant";
    public const string This = "this";
    public const string That = "that";
    public const string Pointer = "pointer";
    public const string Temp = "temp";

    public static readonly Dictionary<string, string> Shorthand = new() {
        { Segment.Argument, "ARG" },
        { Segment.Local, "LCL" },
        { Segment.This, "THIS" },
        { Segment.That, "THAT" }
    };
}

public enum CommandType
{
    Arithmetic,
    Push,
    Pop,
    Label,
    Goto,
    If,
    Function,
    Return,
    Call,
    Comment,
    EmptyLine
}
