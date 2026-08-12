using System.Reflection;

var asm = typeof(AvaloniaEdit.TextEditor).Assembly;
var interesting = new[] { "Completion", "Hover", "ToolTip", "ICompletionData" };
foreach (var t in asm.GetTypes().Where(t => interesting.Any(i => t.FullName!.Contains(i))).OrderBy(t => t.FullName))
{
    Console.WriteLine("TYPE " + t.FullName + (t.IsInterface ? " (interface)" : ""));
    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
    {
        if (m is MethodInfo mi && mi.IsSpecialName) continue;
        if (m.Name is "get_Item" or "set_Item") continue;
        Console.WriteLine("    " + m.MemberType + " " + m.Name);
    }
}

Console.WriteLine("\n== TextArea events ==");
foreach (var e in typeof(AvaloniaEdit.Editing.TextArea).GetEvents())
    Console.WriteLine("  event " + e.Name + " : " + e.EventHandlerType?.Name);

Console.WriteLine("\n== TextEditor members (subset) ==");
foreach (var m in typeof(AvaloniaEdit.TextEditor).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
{
    if (m.MemberType is MemberTypes.Method && ((MethodInfo)m).IsSpecialName) continue;
    Console.WriteLine("  " + m.MemberType + " " + m.Name);
}

Console.WriteLine("\n== PointerHoverLogic ==");
var asm2 = typeof(AvaloniaEdit.TextEditor).Assembly;
var phl = asm2.GetType("AvaloniaEdit.Rendering.PointerHoverLogic")!;
foreach (var c in phl.GetConstructors())
    Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
foreach (var e in phl.GetEvents())
    Console.WriteLine("  event " + e.Name + " : " + e.EventHandlerType?.Name);

Console.WriteLine("\n== TextView hover/tooltip members ==");
foreach (var t in asm2.GetTypes().Where(t => t.Name == "TextView" && t.Namespace == "AvaloniaEdit"))
{
    Console.WriteLine("FOUND " + t.FullName);
    foreach (var e in t.GetEvents())
        Console.WriteLine("  event " + e.Name + " : " + e.EventHandlerType?.Name);
}

Console.WriteLine("\n== CompletionWindow ctor ==");
var cw = asm2.GetType("AvaloniaEdit.CodeCompletion.CompletionWindow")!;
foreach (var c in cw.GetConstructors())
    Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");

Console.WriteLine("\n== ICompletionData ==");
var icd = asm2.GetType("AvaloniaEdit.CodeCompletion.ICompletionData")!;
foreach (var m in icd.GetMembers())
    Console.WriteLine("  " + m.MemberType + " " + m.Name);

Console.WriteLine("\n== TextArea caret-related ==");
var ta2 = asm2.GetType("AvaloniaEdit.Editing.TextArea")!;
foreach (var p in ta2.GetProperties())
    if (p.Name.Contains("Caret") || p.Name == "TextView" || p.Name == "Document")
        Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);
var caret = asm2.GetType("AvaloniaEdit.Editing.Caret")!;
foreach (var p in caret.GetProperties())
    Console.WriteLine("  caret prop " + p.PropertyType.Name + " " + p.Name);
foreach (var e in caret.GetEvents())
    Console.WriteLine("  caret event " + e.Name + " : " + e.EventHandlerType?.Name);

Console.WriteLine("\n== PointerHoverEventArgs ==");
var phe = asm2.GetTypes().FirstOrDefault(t => t.Name.Contains("PointerHover"))!;
Console.WriteLine(phe.FullName);
foreach (var p in phe.GetProperties())
    Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);
foreach (var f in phe.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
    Console.WriteLine("  field " + f.FieldType.Name + " " + f.Name);

Console.WriteLine("\n== GetPositionFromPoint signature ==");
var te = typeof(AvaloniaEdit.TextEditor);
foreach (var m in te.GetMethods().Where(m => m.Name == "GetPositionFromPoint"))
    Console.WriteLine("  " + m.ReturnType.Name + " GetPositionFromPoint(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")");

Console.WriteLine("\n== ISegment (completion) ==");
var seg = asm2.GetType("AvaloniaEdit.Document.ISegment")!;
foreach (var p in seg.GetProperties())
    Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);

Console.WriteLine("\n== TextViewPosition ==");
var tvp = asm2.GetType("AvaloniaEdit.Document.TextViewPosition");
if (tvp != null)
{
    foreach (var p in tvp.GetProperties())
        Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);
}
else Console.WriteLine("  MISSING TextViewPosition");

Console.WriteLine("\n== PointerHover event args ==");
foreach (var t in asm2.GetTypes().Where(t => t.Name == "PointerHoverEventArgs"))
{
    Console.WriteLine("TYPE " + t.FullName + " base=" + t.BaseType?.Name);
    foreach (var p in t.GetProperties())
        Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);
}

Console.WriteLine("\n== CompletionList.CompletionData ==");
var cl = asm2.GetType("AvaloniaEdit.CodeCompletion.CompletionList")!;
foreach (var p in cl.GetProperties())
    Console.WriteLine("  prop " + p.PropertyType.Name + " " + p.Name);

Console.WriteLine("\n== KeyGesture / KeyBindings ==");
foreach (var t in asm2.GetTypes().Where(t => t.Namespace == "AvaloniaEdit" && (t.Name.Contains("KeyBinding"))))
    Console.WriteLine("TYPE " + t.FullName);

Console.WriteLine("\n== TextEditor.PointerHover handler type ==");
var te2 = typeof(AvaloniaEdit.TextEditor);
foreach (var e in te2.GetEvents())
    if (e.Name.Contains("Hover"))
    {
        var invoker = e.EventHandlerType!.GetMethod("Invoke")!;
        Console.WriteLine(e.Name + " : " + e.EventHandlerType.Name + " -> " + string.Join(", ", invoker.GetParameters().Select(p => p.ParameterType.FullName)));
    }

Console.WriteLine("\n== TextViewPosition lookup ==");
foreach (var t in asm2.GetTypes().Where(t => t.Name.Contains("TextViewPosition")))
    Console.WriteLine("FOUND " + t.FullName);






