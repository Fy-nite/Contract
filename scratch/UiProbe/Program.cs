// Headless end-to-end probe for the Ui-binding plumbing:
//   lambda -> native binding (object handle) -> InvokeDelegate -> VM callback
// Mirrors UiBinding's exact method shapes without needing Avalonia.

using Contract.Runtime;
using UiProbe;

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string name, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failures.Add(name);
        Console.WriteLine($"  FAIL  {name}{(detail != null ? $"\n        {detail}" : "")}");
    }
}

// ── The program under test ─────────────────────────────────────────────────

const string Source = """
<NativeBinding("FakeUi")>
Contract Window {
    fn SetTitle(title: string) { }
    fn AddLabel(text: string) -> object { }
    fn SetText(control: object, text: string) { }
    fn GetText(control: object) -> string { }
    fn AddButton(text: string, callback: () -> void) -> object { }
    fn AddCheckBox(text: string, callback: () -> void) -> object { }
    fn IsChecked(control: object) -> bool { }
    fn SetChecked(control: object, checked: bool) { }
    fn Show() { }
}

Contract Program {
    static clicks: int;
    static window: object;
    static log: object;

    static fn Main() {
        var w = new Window();
        Program.window = w;
        w.SetTitle("probe");

        Program.log = w.AddLabel("ready");

        // Capturing lambda (log + w) stored as a callback handle.
        w.AddButton("go", fun -> {
            Program.clicks = Program.clicks + 1;
            w.SetText(Program.log, "clicked " + Convert.ToString(Program.clicks));
        });

        // Lambda reading its own control's state through the handle.
        var box: object = null;
        box = w.AddCheckBox("dark", fun -> {
            w.SetText(Program.log, "dark is " + Convert.ToStringB(w.IsChecked(box)));
        });
        w.SetChecked(box, true);

        w.Show();
        if (Program.window != null) {
            IO.Println("window is up");
        }
    }
}
""";

// ── Drive it ───────────────────────────────────────────────────────────────

var runtime = new ContractRuntime();
runtime.RegisterBinding("FakeUi", typeof(FakeUi));
FakeUi.Runtime = runtime;

var ir = ContractCompiler.CompileSource(Source, null, out var diags, new[] { typeof(FakeUi).Assembly });
if (diags.HasErrors)
{
    Console.WriteLine("compile diagnostics:");
    foreach (var d in diags.Diagnostics) Console.WriteLine("  " + d);
    return 1;
}


runtime.RunModule(runtime.LoadTextModule(ir!));

var win = runtime.Inner.GetStaticField("Program.window");
var log = runtime.Inner.GetStaticField("Program.log");

Check(FakeUi.WindowTitle == "probe", "window created + titled", $"title='{FakeUi.WindowTitle}'");
Check(FakeUi.LastText == "dark is True", "checkbox callback ran (self-read handle)", $"text='{FakeUi.LastText}'");
Check(FakeUi.GetText(win!, log!) == "dark is True", "label handle round-trips GetText", $"text='{FakeUi.GetText(win!, log!)}'");

// Invoke the stored button callback through the runtime — the UI-thread path.
FakeUi.Runtime!.InvokeDelegate(FakeUi.ButtonCallback!);
Check(FakeUi.LastText == "clicked 1", "button callback invoked via InvokeDelegate", $"text='{FakeUi.LastText}'");
Check(Convert.ToInt32(runtime.Inner.GetStaticField("Program.clicks")) == 1, "callback mutated VM static state");

// A second click: captured state is by-reference, so the counter persists.
FakeUi.Runtime!.InvokeDelegate(FakeUi.ButtonCallback!);
Check(FakeUi.LastText == "clicked 2" && Convert.ToInt32(runtime.Inner.GetStaticField("Program.clicks")) == 2,
    "second click sees updated capture (by-reference)");

Console.WriteLine($"\n== Probe results: {passed} passed, {failures.Count} failed ==");
return failures.Count == 0 ? 0 : 1;

