using System.Text;

namespace HackTranslator;

public class Program
{
    public static int Main(string[] args)
    {
        var path = args[0];
        try
        {
            var fileContent = File.ReadAllLines(path);
            var translator = new Translator(fileContent);
            Console.Write(translator.ProcessFile());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Can't open file at path '{path}': {ex}");
        }

        return 0;
    }
}

public class Translator
{
    private string[] _originalFile;

    public Translator(string[] fileContent)
    {
        _originalFile = fileContent;
    }

    public string ProcessFile()
    {
        var outputBuilder = new StringBuilder();

        outputBuilder.AppendLine("// initialize stack pointer");
        outputBuilder.AppendLine("@256");
        outputBuilder.AppendLine("D=A");
        outputBuilder.AppendLine("@SP");
        outputBuilder.AppendLine("M=D");

        foreach (var vmCommand in _originalFile)
        {
            var command = Parser.Parse(vmCommand);
            var codeWriter = new CodeWriter();
            var outputCode = codeWriter.WriteCode(command);
            foreach (var hackCommand in outputCode)
            {
                outputBuilder.AppendLine(hackCommand);
            }
        }

        outputBuilder.AppendLine("// final infinite loop");
        outputBuilder.AppendLine("(END)");
        outputBuilder.AppendLine("@END");
        outputBuilder.AppendLine("0;JMP");

        return outputBuilder.ToString();
    }
}

public static class Parser
{
    public static Command Parse(string vmCommand)
    {
        if (string.IsNullOrWhiteSpace(vmCommand))
        {
            return new() {
                Type = CommandType.EmptyLine,
                FirstArgument = null,
                SecondArgument = null
            };
        }
        else if (vmCommand.StartsWith("//"))
        {
            return new() {
                Type = CommandType.Comment,
                FirstArgument = null,
                SecondArgument = null
            };
        }

        var parts = vmCommand.Split(' ');
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
            _ => new() { Type = CommandType.Comment, FirstArgument = null, SecondArgument = null }
        };
    }
}

public class CodeWriter
{
    private static int labelCount = 0;

    public string[] WriteCode(Command command)
    {
        return command switch {
            { Type: CommandType.Arithmetic } => WriteALU(command.FirstArgument!),
            { Type: CommandType.Push } => WritePush(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Pop } => WritePop(command.FirstArgument!, command.SecondArgument!.Value),
            { Type: CommandType.Comment } => [],
            _ => []
        };
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

    private string WriteLabel(string labelBase)
    {
        labelCount++;
        return $"{labelBase}.{labelCount}";
    }

    private static string[] WritePush(string segment, int index)
    => segment switch
    {
        Segment.Constant => [ $"// push constant {index}", $"@{index}", "D=A", ..PushOntoStack() ],
        Segment.Static => [ $"// push static {index}", $"@{index+16}", "D=M", ..PushOntoStack() ],
        Segment.Temp => [ $"// push temp {index}", $"@{index+5}", "D=M", ..PushOntoStack() ],
        Segment.Pointer => [ $"// push pointer {index}", $"@R{index+3}", "D=M", ..PushOntoStack() ],
        Segment.Local or Segment.Argument or Segment.This or Segment.That => [ $"// push {segment} {index}", ..PullFromNormalSegment(segment, index), ..PushOntoStack() ],
        _ => throw new InvalidOperationException($"Illegal segment of push command: {segment}")
    };

    private static string[] WritePop(string segment, int index)
    => segment switch {
        Segment.Static => [ $"// pop static {index}", ..PopFromStack(), ..PutIntoStatic(index) ],
        Segment.Temp => [ $"// pop temp {index}", ..PopFromStack(), ..PutIntoTemp(index) ],
        Segment.Pointer => [ $"// pop pointer {index}", ..PopFromStack(), $"@R{index+3}", "M=D" ],
        Segment.Local or Segment.Argument or Segment.This or Segment.That => [ $"// pop {segment} {index}", ..PopFromStack(), ..PutIntoNormalSegment(segment, index) ],
        _ => throw new InvalidOperationException($"Illegal segment of pop command: {segment}")
    };

    private string[] WriteEq()
    {
        var endOfEqualityCheckLabel = WriteLabel("EQEND");
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
        var endOfEqualityCheckLabel = WriteLabel("GTEND");
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
        var endOfEqualityCheckLabel = WriteLabel("LTEND");
        return [
            "// gt",
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

    // static variables have base address of 16
    private static string[] PutIntoStatic(int index)
        => [ $"@{index+16}", "M=D" ];

    private static string[] PutIntoTemp(int index)
        => [ $"@{index+5}", "M=D" ];

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
