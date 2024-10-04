namespace Game;

public static class Log
{
    public const ConsoleColor TraceColor = ConsoleColor.Gray;
    public const char TracePrefix = ' ';

    public const char InfoPrefix = '-';

    public const ConsoleColor WarnColor = ConsoleColor.Yellow;
    public const char WarnPrefix = '!';

    public const ConsoleColor ErrorColor = ConsoleColor.Red;
    public const char ErrorPrefix = '#';

    public static void Trace(in string text)
    {
        Console.ForegroundColor = TraceColor;
        Console.WriteLine($"{TracePrefix} {text}");
        Console.ResetColor();
    }

    public static void Info(in string text)
    {
        Console.WriteLine($"{InfoPrefix} {text}");
    }

    public static void Warn(in string text)
    {
        Console.ForegroundColor = WarnColor;
        Console.WriteLine($"{WarnPrefix} {text}");
        Console.ResetColor();
    }

    public static void Error(in string text)
    {
        Console.ForegroundColor = ErrorColor;
        Console.WriteLine($"{ErrorPrefix} {text}");
        Console.ResetColor();
    }

    public class Fatal : Exception
    {
        public Fatal(string message) : base()
        {
            Console.ForegroundColor = Log.ErrorColor;
            Console.WriteLine($"{Log.ErrorPrefix} {message}");
            Console.ResetColor();
        }

        public override string StackTrace => "";
    }
}
