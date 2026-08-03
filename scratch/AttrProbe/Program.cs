// One-off probe: load a compiled .orbt and dump type/method attributes.
using ObjectRT.Abstractions;
using ObjectRT.Reader;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: AttrProbe <file.orbt>");
    return 1;
}

var module = OrbtFileReader.ReadFile(args[0]);
int typeAttrs = 0, methodAttrs = 0;
foreach (var t in module.Types)
{
    foreach (var a in t.Attributes)
    {
        var argText = string.Join(", ", a.ArgIndices.Select(i => module.Resolve(i)));
        Console.WriteLine($"type {module.Resolve(t.NameIndex)}: @{module.Resolve(a.NameIndex)}({argText})");
        typeAttrs++;
    }
    foreach (var m in t.Methods)
    {
        foreach (var a in m.Attributes)
        {
            var argText = string.Join(", ", a.ArgIndices.Select(i => module.Resolve(i)));
            Console.WriteLine($"  method {module.Resolve(m.NameIndex)}: @{module.Resolve(a.NameIndex)}({argText})");
            methodAttrs++;
        }
    }
}
Console.WriteLine($"type attrs: {typeAttrs}, method attrs: {methodAttrs}");
return 0;
