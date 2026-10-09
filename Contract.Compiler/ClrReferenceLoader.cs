using System.Reflection;
using Contract.Compiler.AST;
using Contract.Compiler.Diagnostics;
using Contract.Compiler.StandardLibrary;

namespace Contract.Compiler;

/// <summary>
/// Links a real .NET assembly into a Contract compilation by synthesizing
/// external <c>ClrImport</c> contracts from its public surface. This is the
/// "reference the DLL" path: once an assembly is linked, scripts call its
/// types by their real CLR name (<c>System.Math.Abs(...)</c>,
/// <c>V12.Core.Element(...)</c>) with no hand-written facade and no
/// <c>[ClassBinding]</c> wrapper.
///
/// Unlike <see cref="CompiledReferenceLoader"/> (which statically links an
/// ObjektIL module), each synthesized contract is marked
/// <see cref="ContractDeclaration.IsExternal"/> and carries a
/// <see cref="ContractDeclaration.ClrImportType"/>, so call sites are emitted
/// as <c>Type.Method</c> targets and resolved at runtime through the CLR
/// reflection resolver.
/// </summary>
public static class ClrReferenceLoader
{
    /// <summary>
    /// Links the explicit <paramref name="explicitAssemblies"/> plus every
    /// assembly named in the source by
    /// <c>&lt;AssemblyRef("Name")&gt;</c> / <c>&lt;AssemblyRef(Path: "x.dll")&gt;</c>.
    /// Must run after parsing and before analysis. Resolution failures are
    /// reported on <paramref name="diagnostics"/> (when supplied).
    /// </summary>
    public static void LinkFromProgram(
        Program program,
        string? fileName,
        IEnumerable<Assembly>? explicitAssemblies,
        DiagnosticBag? diagnostics = null,
        IEnumerable<string>? reservedNames = null)
    {
        var assemblies = new List<Assembly>();
        if (explicitAssemblies != null)
            assemblies.AddRange(explicitAssemblies.Where(a => a != null));

        string? sourceDir = string.IsNullOrEmpty(fileName)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(fileName));

        foreach (var contract in program.Contracts)
        {
            foreach (var attr in contract.Attributes)
            {
                if (!attr.Name.Equals("AssemblyRef", StringComparison.OrdinalIgnoreCase)) continue;
                string? target = attr.NamedArguments.TryGetValue("Path", out var path) ? path
                    : attr.NamedArguments.TryGetValue("Name", out var named) ? named
                    : attr.Arguments.FirstOrDefault();
                if (string.IsNullOrEmpty(target)) continue;
                var asm = ResolveAssembly(target!, sourceDir);
                if (asm == null)
                {
                    diagnostics?.AddError(
                        $"AssemblyRef '{target}' could not be resolved (not loaded and not found relative to the source file)",
                        attr.Line, attr.Column);
                    continue;
                }
                assemblies.Add(asm);
            }
        }

        LinkAssemblies(program, assemblies, reservedNames);
    }
    /// <summary>
    /// Synthesizes Contract declarations for every public type in each linked
    /// assembly into <paramref name="program"/>. Idempotent: a type already
    /// declared (by a user contract or an earlier link) is skipped. Names in
    /// <paramref name="reservedNames"/> (registered binding modules) are also
    /// skipped, so an assembly-link can never shadow a hand-written facade.
    /// </summary>
    public static void LinkAssemblies(Program program, IEnumerable<Assembly> assemblies, IEnumerable<string>? reservedNames = null)
    {
        var loaded = assemblies.Where(a => a != null).Distinct().ToList();
        if (loaded.Count == 0) return;

        // Collect the public, non-nested, non-generic-definition types we can
        // model. Generic type definitions and nested types are skipped in v1:
        // the synthesized facade cannot express their arity/qualification yet.
        var types = new List<Type>();
        foreach (var asm in loaded)
        {
            foreach (var t in TypeLoader.GetLoadableTypes(asm))
            {
                if (!t.IsPublic) continue;
                if (t.IsNested) continue;
                if (t.IsGenericTypeDefinition) continue;
                if (t.IsPointer || t.IsByRef) continue;
                if (typeof(Delegate).IsAssignableFrom(t)) continue;   // no callable facade
                types.Add(t);
            }
        }

        // Existing declarations win: never shadow a user contract or stdlib.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in program.Contracts) { taken.Add(c.Name); taken.Add(c.FullName); }
        foreach (var e in program.Enums) { taken.Add(e.Name); taken.Add(e.FullName); }
        foreach (var s in program.Structs) { taken.Add(s.Name); taken.Add(s.FullName); }
        // Registered [ClassBinding] / host module names are reserved too: the
        // bound facade owns that name, and a real CLR type that happens to
        // share it (e.g. the engine's V12.Registry class vs the
        // "V12.Registry" binding) must not take it over.
        if (reservedNames != null)
        {
            foreach (var name in reservedNames)
            {
                if (!string.IsNullOrEmpty(name)) taken.Add(name!);
            }
        }

        // Map each linkable type to the Contract type name it is exposed as.
        var typeNames = new Dictionary<Type, string>();
        foreach (var t in types)
            typeNames[t] = t.FullName ?? t.Name;

        // Enums first so class members can reference them.
        foreach (var t in types.Where(t => t.IsEnum))
        {
            string full = typeNames[t];
            if (!taken.Add(full)) continue;
            var (ns, shortName) = SplitQualifiedName(full);
            taken.Add(shortName);
            var enumDecl = new EnumDeclaration(shortName, 1, 1) { Namespace = ns, IsExternal = true };
            foreach (var name in Enum.GetNames(t))
                enumDecl.Members.Add(name);
            program.Enums.Add(enumDecl);
        }

        // Classes, structs and interfaces — exposed as external ClrImport
        // contracts. Delegates are skipped (no callable facade).
        foreach (var t in types)
        {
            if (t.IsEnum) continue;
            if (typeof(Delegate).IsAssignableFrom(t)) continue;

            string full = typeNames[t];
            if (!taken.Add(full)) continue;
            var (ns, shortName) = SplitQualifiedName(full);
            taken.Add(shortName);

            var contract = SynthesizeType(t, ns, shortName, full, typeNames);
            if (contract != null)
                program.Contracts.Add(contract);
        }

        // Extension classes (C# static classes with [Extension] methods): emit
        // extend declarations over the linked receiver types so scripts call
        // them with member syntax — owner.GetOrAddTransform() dispatches to
        // ElementExtensions.GetOrAddTransform(owner). Registered under both the
        // receiver's full and short facade names. Optional parameters are
        // dropped from the synthesized signatures (call sites pass fewer args;
        // the runtime resolver pads CLR defaults).
        foreach (var t in types)
        {
            if (!(t.IsAbstract && t.IsSealed)) continue;   // C# static class
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "ExtensionAttribute")) continue;
                if (m.IsGenericMethod || m.ContainsGenericParameters) continue;

                var ps = m.GetParameters();
                if (ps.Length == 0) continue;
                if (ps[0].ParameterType.IsByRef || ps[0].ParameterType.IsPointer) continue;
                if (!typeNames.TryGetValue(ps[0].ParameterType, out var targetFull)) continue;   // linked receivers only

                var returnType = MapType(m.ReturnType, typeNames);
                if (returnType == null) continue;

                var fd = new FunctionDeclaration(m.Name, 1, 1)
                {
                    IsStatic = true,
                    IsExtension = true,
                    ExtensionTargetType = targetFull,
                    IsExternal = true,
                    ContractName = t.FullName ?? t.Name,
                    ReturnType = returnType,
                    Access = AccessModifier.Public,
                };
                bool ok = true;
                for (int i = 1; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (p.ParameterType.IsByRef || p.ParameterType.IsPointer) { ok = false; break; }
                    if (p.HasDefaultValue) continue;   // trailing optional — runtime pads
                    var pt = MapType(p.ParameterType, typeNames);
                    if (pt == null) { ok = false; break; }
                    fd.Parameters.Add(new Parameter(p.Name ?? "arg", pt, 1, 1));
                }
                if (!ok) continue;

                var (_, targetShort) = SplitQualifiedName(targetFull);
                program.Extensions.Add(new ExtendDeclaration(targetFull, 1, 1) { Methods = { fd } });
                if (!string.Equals(targetShort, targetFull, StringComparison.Ordinal))
                    program.Extensions.Add(new ExtendDeclaration(targetShort, 1, 1) { Methods = { fd } });
            }
        }
    }

    /// <summary>
    /// Builds one external <c>ClrImport</c> contract from a CLR type. Returns
    /// null when the type has no usable public surface.
    /// </summary>
    private static ContractDeclaration? SynthesizeType(
        Type type, string? ns, string shortName, string fullName, Dictionary<Type, string> typeNames)
    {
        var contract = new ContractDeclaration(shortName, 1, 1)
        {
            Namespace = ns,
            IsExternal = true,
            ClrImportType = fullName,
        };

        bool hasInstanceMethod = false;

        // Non-special, non-generic public methods (including inherited ones,
        // so the facade mirrors the type as callers see it).
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (m.IsSpecialName) continue;          // accessors, operators
            if (m.IsGenericMethod) continue;        // generic methods unsupported in v1
            if (m.ContainsGenericParameters) continue;

            var returnType = MapType(m.ReturnType, typeNames);
            if (returnType == null) continue;       // by-ref/pointer/unsupported return

            var parameters = new List<Parameter>();
            bool ok = true;
            foreach (var p in m.GetParameters())
            {
                if (p.ParameterType.IsByRef || p.ParameterType.IsPointer) { ok = false; break; }
                var pt = MapType(p.ParameterType, typeNames);
                if (pt == null) { ok = false; break; }
                parameters.Add(new Parameter(p.Name ?? "arg", pt, 1, 1));
            }
            if (!ok) continue;

            var fd = new FunctionDeclaration(m.Name, 1, 1)
            {
                IsStatic = m.IsStatic,
                IsInstance = !m.IsStatic,
                ContractName = fullName,
                ReturnType = returnType,
                Access = AccessModifier.Public,
            };
            foreach (var p in parameters) fd.Parameters.Add(p);
            contract.Members.Add(fd);
            if (!m.IsStatic) hasInstanceMethod = true;
        }

        // Public fields and properties — modeled as fields so instance
        // field/property reads and writes dispatch through the CLR resolver.
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (f.IsSpecialName) continue;
            var ft = MapType(f.FieldType, typeNames);
            if (ft == null) continue;
            contract.Fields.Add(new StructField(f.Name, ft, 1, 1)
            {
                IsStatic = f.IsStatic,
                Access = AccessModifier.Public,
            });
        }
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (p.IsSpecialName) continue;
            if (p.GetIndexParameters().Length > 0) continue;   // indexers unsupported in v1
            if (p.GetMethod == null && p.SetMethod == null) continue;
            var pt = MapType(p.PropertyType, typeNames);
            if (pt == null) continue;
            if (contract.Fields.Any(existing => existing.Name == p.Name)) continue;
            bool isStatic = (p.GetMethod ?? p.SetMethod)!.IsStatic;
            contract.Fields.Add(new StructField(p.Name, pt, 1, 1)
            {
                IsStatic = isStatic,
                Access = AccessModifier.Public,
            });
        }

        // A type with instance methods but no public fields/properties still
        // needs one instance field so the analyzer treats its methods as
        // instance-bound (the ClrImport instance-facade heuristic keys off a
        // declared instance field).
        if (hasInstanceMethod && !contract.Fields.Any(f => !f.IsStatic))
        {
            contract.Fields.Add(new StructField("__handle", new TypeDescriptor.Named("object"), 1, 1)
            {
                IsStatic = false,
                Access = AccessModifier.Public,
            });
        }

        // Ensure the type is always a valid Contract type name even when it
        // exposes no usable members (a marker/attribute/static class): add a
        // synthetic instance field so references to it resolve and the analyzer
        // treats its members as instance-bound where relevant.
        if (contract.Members.Count == 0 && contract.Fields.Count == 0)
        {
            contract.Fields.Add(new StructField("__handle", new TypeDescriptor.Named("object"), 1, 1)
            {
                IsStatic = false,
                Access = AccessModifier.Public,
            });
        }

        return contract;
    }

    /// <summary>
    /// Maps a CLR type to a Contract <see cref="TypeDescriptor"/>, or null when
    /// the type cannot be represented (by-ref, pointer, open generic, generic
    /// parameter). Primitive/language mappings win; a type that is itself part
    /// of the linked set maps to its exposed full name; every other reference
    /// type degrades to <c>object</c>.
    /// </summary>
    internal static TypeDescriptor? MapType(Type t, Dictionary<Type, string> typeNames)
    {
        if (t.IsByRef || t.IsPointer) return null;
        if (t.IsGenericParameter) return null;
        if (t.IsArray)
        {
            var element = MapType(t.GetElementType()!, typeNames);
            return element == null ? null : new TypeDescriptor.ArrayOf(element);
        }

        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying != null) return MapType(underlying, typeNames);

        if (t == typeof(void)) return new TypeDescriptor.Named("void");
        if (t == typeof(string)) return new TypeDescriptor.Named("string");
        if (t == typeof(bool)) return new TypeDescriptor.Named("bool");
        if (t == typeof(double)) return new TypeDescriptor.Named("double");
        if (t == typeof(float)) return new TypeDescriptor.Named("float");
        if (t == typeof(long) || t == typeof(ulong)) return new TypeDescriptor.Named("int64");
        if (t == typeof(int) || t == typeof(uint) || t == typeof(short)
            || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte))
            return new TypeDescriptor.Named("int");
        if (t == typeof(object)) return new TypeDescriptor.Named("object");
        if (t.IsEnum) return new TypeDescriptor.Named("int");

        if (t.IsGenericType) return new TypeDescriptor.Named("object");   // List<T>, etc.

        if (typeNames.TryGetValue(t, out var full))
            return new TypeDescriptor.Named(full);

        return new TypeDescriptor.Named("object");
    }

    private static (string? Namespace, string Name) SplitQualifiedName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot > 0
            ? (fullName[..dot], fullName[(dot + 1)..])
            : (null, fullName);
    }

    /// <summary>
    /// Resolves an <c>&lt;AssemblyRef(...)&gt;</c> target (a simple name or a
    /// path, relative to <paramref name="sourceDir"/>) to a loaded assembly.
    /// Returns null when it cannot be found.
    /// </summary>
    public static Assembly? ResolveAssembly(string name, string? sourceDir)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            name = name[1..^1];

        // Path form: a file that exists relative to the source directory.
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.Contains('/') || name.Contains('\\'))
        {
            string candidate = Path.IsPathRooted(name)
                ? name
                : Path.Combine(sourceDir ?? Environment.CurrentDirectory, name);
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); }
                catch { /* fall through to name resolution */ }
            }
        }

        // Simple-name form: prefer an assembly already in the process, then
        // ask the loader.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            try
            {
                if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(asm.FullName, name, StringComparison.OrdinalIgnoreCase))
                    return asm;
            }
            catch { }
        }

        try { return Assembly.Load(new AssemblyName(name)); }
        catch
        {
            try { return Assembly.Load(name); }
            catch { return null; }
        }
    }
}
