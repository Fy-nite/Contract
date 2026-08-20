using ObjektRT.Core.Attributes;

namespace ObjektRT.Stdlib.System;

/// <summary>
/// Console control: colors, cursor, and key input. A generic ObjektRT stdlib
/// module. Complements the <c>IO</c> module (which handles plain text output).
/// </summary>
[ClassBinding("Console")]
public static class Console
{
    /// <summary>Sets the foreground text color. Names: black, darkblue, darkgreen, darkcyan, darkred, darkmagenta, darkyellow, gray, darkgray, blue, green, cyan, red, magenta, yellow, white.</summary>
    public static void SetForeground(string color)
    {
        try { global::System.Console.ForegroundColor = ParseColor(color); } catch { }
    }

    /// <summary>Sets the background color. Names as <see cref="SetForeground"/>.</summary>
    public static void SetBackground(string color)
    {
        try { global::System.Console.BackgroundColor = ParseColor(color); } catch { }
    }

    /// <summary>Resets the console colors to the defaults.</summary>
    public static void ResetColor() => global::System.Console.ResetColor();

    /// <summary>Clears the console screen.</summary>
    public static void Clear() => global::System.Console.Clear();

    /// <summary>Moves the cursor to the given (1-based) row and column.</summary>
    public static void SetCursorPosition(int left, int top)
    {
        try { global::System.Console.SetCursorPosition(left, top); } catch { }
    }

    /// <summary>Reads a single key press and returns the character as a string ("" for non-character keys).</summary>
    public static string ReadKey()
    {
        var key = global::System.Console.ReadKey(intercept: true);
        return key.KeyChar.ToString();
    }

    /// <summary>True when a key is waiting to be read.</summary>
    public static bool KeyAvailable() => global::System.Console.KeyAvailable;

    /// <summary>Writes a value to standard output without a newline (alias of IO.Print).</summary>
    public static void Write(object contents) => global::System.Console.Write(contents);

    /// <summary>Writes a value to standard output followed by a newline (alias of IO.Println).</summary>
    public static void WriteLine(object contents) => global::System.Console.WriteLine(contents);

    private static global::System.ConsoleColor ParseColor(string name) => name.ToLowerInvariant() switch
    {
        "black" => global::System.ConsoleColor.Black,
        "darkblue" => global::System.ConsoleColor.DarkBlue,
        "darkgreen" => global::System.ConsoleColor.DarkGreen,
        "darkcyan" => global::System.ConsoleColor.DarkCyan,
        "darkred" => global::System.ConsoleColor.DarkRed,
        "darkmagenta" => global::System.ConsoleColor.DarkMagenta,
        "darkyellow" => global::System.ConsoleColor.DarkYellow,
        "gray" => global::System.ConsoleColor.Gray,
        "darkgray" => global::System.ConsoleColor.DarkGray,
        "blue" => global::System.ConsoleColor.Blue,
        "green" => global::System.ConsoleColor.Green,
        "cyan" => global::System.ConsoleColor.Cyan,
        "red" => global::System.ConsoleColor.Red,
        "magenta" => global::System.ConsoleColor.Magenta,
        "yellow" => global::System.ConsoleColor.Yellow,
        "white" => global::System.ConsoleColor.White,
        _ => global::System.ConsoleColor.Gray,
    };
}
