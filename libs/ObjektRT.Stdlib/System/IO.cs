using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// Console input/output. A generic ObjektRT stdlib module — hosts register it
/// under any name (e.g. "ObjektRT.Stdlib.System.IO" or a short alias) via the
/// runtime's CLR type registry.
/// </summary>
[ClassBinding("IO")]
public static class IO
{
    /// <summary>Prints a value to standard output without a trailing newline.</summary>
    public static void Print(object contents) => global::System.Console.Write(contents);

    /// <summary>Prints a value to standard output followed by a newline.</summary>
    public static void Println(object contents) => global::System.Console.WriteLine(contents);

    /// <summary>Reads a single line from standard input ("" on EOF).</summary>
    public static string Readln() => global::System.Console.ReadLine() ?? "";
}
