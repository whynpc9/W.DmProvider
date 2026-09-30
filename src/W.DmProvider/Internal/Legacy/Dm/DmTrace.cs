using System;

namespace W.Dm;

/// <summary>Compatibility facade. Legacy process-wide tracing is disabled.</summary>
public class DmTrace
{
    private DmTrace() { }

    public static bool To_file
    {
        get => false;
        set { /* The legacy output destination is inactive. */ }
    }

    public static TraceLevel Level
    {
        get => TraceLevel.None;
        set
        {
            if (value != TraceLevel.None)
                throw new NotSupportedException("Legacy tracing is unsupported.");
        }
    }

    protected static void WriteIntoFile(byte[] info) { }
    protected static void WriteIntoFile(byte[] info, int thd) { }
    internal static void TracePropertySet(TraceLevel lev, string className, string propertyName) { }
    internal static void TracePropertyGet(TraceLevel lev, string className, string propertyName) { }
    internal static void TraceMethodEnter(TraceLevel lev, string className, string methodName) { }
    public static void TracePrint(string str) { }
    public static void TracePrintStack(string usrString) { }
    public static void TracePrintStack() { }
}
