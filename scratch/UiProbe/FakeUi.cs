using Contract.Compiler.StandardLibrary;
using Contract.Runtime;
using ObjektRT.Core.Attributes;

namespace UiProbe;

/// <summary>
/// Fake host binding mirroring UiBinding's exact method shapes: window as
/// argument 0, handles round-trip as external objects, callbacks arrive as
/// delegate handles and are invoked through the runtime.
/// </summary>
[ClassBinding("FakeUi")]
public static class FakeUi
{
    public static ContractRuntime? Runtime;
    public static string WindowTitle = "";
    public static string LastText = "";
    public static readonly Dictionary<object, string> Texts = new();
    public static readonly Dictionary<object, bool> Checks = new();
    public static object? ButtonCallback;
    public static object? CheckCallback;

    [MethodBinding] public static object Create() => new();

    [MethodBinding] public static void SetTitle(object window, string title) => WindowTitle = title;

    [MethodBinding] public static object AddLabel(object window, string text)
    {
        var h = new object();
        Texts[h] = text;
        return h;
    }

    [MethodBinding] public static void SetText(object window, object control, string text)
    {
        Texts[control] = text;
        LastText = text;
    }

    [MethodBinding] public static string GetText(object window, object control)
        => Texts.TryGetValue(control, out var t) ? t : "";

    [MethodBinding] public static object AddButton(object window, string text, object callback)
    {
        ButtonCallback = callback;
        return new();
    }

    [MethodBinding] public static object AddCheckBox(object window, string text, object callback)
    {
        var h = new object();
        Checks[h] = false;
        CheckCallback = callback;
        return h;
    }

    [MethodBinding] public static bool IsChecked(object window, object control)
        => Checks.TryGetValue(control, out var c) && c;

    [MethodBinding] public static void SetChecked(object window, object control, bool value)
    {
        Checks[control] = value;
        // Fires synchronously, like a real control event would on the UI thread.
        Runtime?.InvokeDelegate(CheckCallback!);
    }

    [MethodBinding] public static void Show(object window) { }
}
