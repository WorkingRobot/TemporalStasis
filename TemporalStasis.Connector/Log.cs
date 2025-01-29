namespace TemporalStasis.Connector;

public static class Log
{
    public static void Warn(string message)
    {
        Console.WriteLine($"[WARN] {message}");
    }

    public static void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public static void Verbose(string message)
    {
        Console.WriteLine($"[VERBOSE] {message}");
    }

    public static void Verbose()
    {
        Verbose(string.Empty);
    }

    //

    public static void Output(string message)
    {
        Console.WriteLine($"[OUTPUT] {message}");
    }

    public static void Output(object obj)
    {
        Output(obj.ToString() ?? "null");
    }
}