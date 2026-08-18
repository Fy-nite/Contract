using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;

namespace Contract.Compiler.StandardLibrary
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ClassBindingAttribute : Attribute
    {
        public string Name { get; }
        public ClassBindingAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MethodBindingAttribute : Attribute
    {
        public string? Name { get; }
        public MethodBindingAttribute(string? name = null) => Name = name;
    }

    public class ExternalMethod
    {
        public string ClassName { get; }
        public string MethodName { get; }
        public MethodInfo Info { get; }

        public ExternalMethod(string className, string methodName, MethodInfo info)
        {
            ClassName = className;
            MethodName = methodName;
            Info = info;
        }
    }

    public class SymbolTable
    {
        // Each module maps a method name to its overloads (a list, so
        // same-name methods with different signatures coexist).
        private readonly Dictionary<string, Dictionary<string, List<ExternalMethod>>> _externalBindings = new();
        private readonly Dictionary<string, ContractDeclaration> _userContracts = new();
        private readonly Dictionary<string, StructDeclaration> _userStructs = new();
        private readonly List<string> _importedNamespaces = new();
        private readonly HashSet<string> _usedImports = new();

        /// <summary>
        /// Imports a namespace (e.g. "ObjektRT.Stdlib.System") so its modules
        /// become addressable by their short, last-segment name ("IO").
        /// </summary>
        public void ImportNamespace(string ns)
        {
            string trimmed = ns.Trim();
            if (trimmed.Length > 0 && !_importedNamespaces.Contains(trimmed))
                _importedNamespaces.Add(trimmed);
        }

        /// <summary>
        /// The imported namespaces that actually resolved a short module name
        /// during this compilation. Lets the analyzer flag unused imports.
        /// </summary>
        public IReadOnlyCollection<string> UsedImportedNamespaces => _usedImports;

        public void RegisterAssembly(Assembly assembly)
        {
            foreach (var type in TypeLoader.GetLoadableTypes(assembly))
            {
                var classAttr = type.GetCustomAttribute<ClassBindingAttribute>();
                if (classAttr == null) continue;
                RegisterExternalType(classAttr.Name, type);
            }
        }

        /// <summary>
        /// Registers a CLR type's public static methods as an external module
        /// under the given (possibly dotted) name. Generic — used by the stdlib
        /// catalog so the official stdlib stays free of Contract-specific
        /// attributes.
        /// </summary>
        public void RegisterExternalType(string className, Type type)
        {
            if (!_externalBindings.ContainsKey(className))
                _externalBindings[className] = new();

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue; // property accessors, operators
                var methodAttr = method.GetCustomAttribute<MethodBindingAttribute>();
                string name = methodAttr?.Name ?? method.Name;
                if (!_externalBindings[className].TryGetValue(name, out var list))
                    _externalBindings[className][name] = list = new();
                list.Add(new ExternalMethod(className, name, method));
            }
        }

        /// <summary>Resolves a possibly-short module name to its fully-registered name (honoring namespace imports).</summary>
        private string? ResolveModuleName(string className)
        {
            // Dotted names are already fully qualified.
            if (className.Contains('.'))
                return _externalBindings.ContainsKey(className) ? className : null;

            // Imported namespaces take priority over root exact matches, so
            // `import ObjektRT.Stdlib.System; IO.Println(...)` calls the
            // stdlib IO, not a same-named root module. Resolution through an
            // import is recorded so unused imports can be flagged.
            foreach (var ns in _importedNamespaces)
            {
                string candidate = $"{ns}.{className}";
                if (_externalBindings.ContainsKey(candidate))
                {
                    _usedImports.Add(ns);
                    return candidate;
                }
            }

            if (_externalBindings.ContainsKey(className)) return className;
            return null;
        }

        public void RegisterUserContract(ContractDeclaration contract)
        {
            // Registered under both the short name and the namespace-qualified
            // name so `Foo.Bar()` and `com.example.Foo.Bar()` both resolve.
            _userContracts[contract.Name] = contract;
            if (contract.FullName != contract.Name)
                _userContracts[contract.FullName] = contract;
        }

        public void RegisterUserStruct(StructDeclaration structDecl)
        {
            _userStructs[structDecl.Name] = structDecl;
            if (structDecl.FullName != structDecl.Name)
                _userStructs[structDecl.FullName] = structDecl;
        }

        public bool TryGetMethod(string className, string methodName, out object? method)
        {
            method = null;
            string? resolved = ResolveModuleName(className);
            if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list) && list.Count > 0)
            {
                method = list[0];
                return true;
            }
            
            if (_userContracts.TryGetValue(className, out var contract))
            {
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func && func.Name == methodName)
                    {
                        method = func;
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Resolves a method by name and argument count, picking the external
        /// overload whose parameter count matches. Falls back to the first
        /// overload when no arity matches (so the call still resolves and the
        /// runtime reports the mismatch). User-contract functions resolve by
        /// name (no overloads). Returns false when the module or method name
        /// is unknown.
        /// </summary>
        public bool TryResolveMethod(string className, string methodName, int argCount, out object? method)
        {
            method = null;
            string? resolved = ResolveModuleName(className);
            if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list) && list.Count > 0)
            {
                var match = list.FirstOrDefault(m => m.Info.GetParameters().Length == argCount);
                method = match ?? list[0];
                return true;
            }

            if (_userContracts.TryGetValue(className, out var contract))
            {
                foreach (var member in contract.Members)
                {
                    if (member is FunctionDeclaration func && func.Name == methodName)
                    {
                        method = func;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>All overloads of a method name on a module, or an empty list when unknown.</summary>
        public IReadOnlyList<ExternalMethod> GetMethodOverloads(string className, string methodName)
        {
            string? resolved = ResolveModuleName(className);
            if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list))
                return list;
            return Array.Empty<ExternalMethod>();
        }

        public bool TryGetStruct(string structName, out StructDeclaration? structDecl)
        {
            return _userStructs.TryGetValue(structName, out structDecl);
        }

        public IEnumerable<string> GetBoundClasses()
        {
            var names = _externalBindings.Keys
                .Concat(_userContracts.Keys)
                .Concat(_userStructs.Keys)
                .ToList();
            // Short last-segment names for modules inside imported namespaces.
            foreach (var ns in _importedNamespaces)
            {
                string prefix = ns + ".";
                foreach (var full in _externalBindings.Keys)
                {
                    if (full.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        string shortName = full.Substring(prefix.Length);
                        if (!names.Contains(shortName)) names.Add(shortName);
                    }
                }
            }
            return names;
        }

        /// <summary>True when <paramref name="className"/> is a bound external stdlib module (IO, Math, ...).</summary>
        public bool IsBoundModule(string className)
            => ResolveModuleName(className) != null;

        /// <summary>True when <paramref name="name"/> is a user-declared contract in this compilation.</summary>
        public bool IsUserContract(string name)
            => _userContracts.ContainsKey(name);

        /// <summary>All external methods registered for a stdlib module (e.g. all of IO's methods).</summary>
        public IEnumerable<ExternalMethod> GetExternalMethods(string className)
        {
            string? resolved = ResolveModuleName(className);
            return resolved != null && _externalBindings.TryGetValue(resolved, out var map)
                ? map.Values.SelectMany(list => list)
                : Enumerable.Empty<ExternalMethod>();
        }
    }
}
