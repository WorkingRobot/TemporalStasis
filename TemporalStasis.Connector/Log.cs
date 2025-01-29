using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TemporalStasis.Connector;

public static class Log
{
    public static void Error(Exception e)
    {
        Console.Error.WriteLine($"[ERROR] {e}");
    }

    public static void Warn(string message)
    {
        Console.WriteLine($"[WARN] {message}");
    }

    public static void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    //

    public static bool IsVerboseEnabled;
    public static void Verbose(DefaultInterpolatedStringHandler message)
    {
        if (IsVerboseEnabled)
            Console.WriteLine($"[VERBOSE] {message.ToStringAndClear()}");
    }

    public static void Verbose(string message)
    {
        if (IsVerboseEnabled)
            Console.WriteLine($"[VERBOSE] {message}");
    }

    public static void Verbose()
    {
        Verbose(string.Empty);
    }

    //

    public static bool IsDebugEnabled;
    public static void Debug(DefaultInterpolatedStringHandler handler)
    {
        if (IsDebugEnabled)
            Console.WriteLine($"[DEBUG] {handler.ToStringAndClear()}");
    }

    public static void Debug(string message)
    {
        if (IsDebugEnabled)
            Console.WriteLine($"[DEBUG] {message}");
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