using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Contract.Compiler.AST;
using ObjektRT.Core.Attributes;

namespace Contract.Compiler.StandardLibrary
{
    public class ExternalMethod
    {
        public string ClassName { get; }
        public string MethodName { get; }
        public MethodInfo Info { get; }

        /// <summary>
        /// True when the method was reached through an instance-style member
        /// chain (<c>this.Module.Classes.Add(node)</c>) on an external helper
        /// module that is really a static call carrying the receiver as its
        /// first argument (<c>List.Add(this.Module.Classes, node)</c>). The
        /// codegen must push the receiver expression first, then the arguments.
        /// </summary>
        public bool ReceiverAsFirstArg { get; set; }

        public ExternalMethod(string className, string methodName, MethodInfo info)
        {
            ClassName = className;
            MethodName = methodName;
            Info = info;
        }
    }

    public class ExternalField
    {
        public string ClassName { get; }
        public string FieldName { get; }
        public MemberInfo Info { get; }
        public Type FieldType { get; }
        public bool IsProperty { get; }

        public ExternalField(string className, string fieldName, MemberInfo info, Type fieldType, bool isProperty)
        {
            ClassName = className;
            FieldName = fieldName;
            Info = info;
            FieldType = fieldType;
            IsProperty = isProperty;
        }
    }

    public class SymbolTable
    {
        // Each module maps a method name to its overloads (a list, so
        // same-name methods with different signatures coexist).
        private readonly Dictionary<string, Dictionary<string, List<ExternalMethod>>> _externalBindings = new();
        private readonly Dictionary<string, Dictionary<string, ExternalField>> _externalFields = new();
        private readonly Dictionary<string, List<ConstructorInfo>> _externalAttributeCtors = new(StringComparer.OrdinalIgnoreCase);
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
        /// under the given (possibly dotted) lookup key. The WIRE name used in
        /// emitted call targets comes from the type's [ClassBinding] name when
        /// present ("__builtin.std.IO" and "ObjektRT.Stdlib.System.IO" both
        /// emit <c>call IO.Println</c>, matching the host bindings), falling
        /// back to the key's last segment. Nothing is implicitly global: short
        /// names resolve only through a namespace import, fully-qualified
        /// spellings resolve anywhere.
        /// </summary>
        public void RegisterExternalType(string className, Type type)
        {
            if (!_externalBindings.ContainsKey(className))
                _externalBindings[className] = new();
            if (!_externalFields.ContainsKey(className))
                _externalFields[className] = new();

            var bindingAttr = type.GetCustomAttribute<ObjektRT.Core.Attributes.ClassBindingAttribute>();
            string wireName = bindingAttr?.Name
                ?? (className.Contains('.') ? className[(className.LastIndexOf('.') + 1)..] : className);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue; // property accessors, operators
                var methodAttr = method.GetCustomAttribute<MethodBindingAttribute>();
                string name = methodAttr?.Name ?? method.Name;
                if (!_externalBindings[className].TryGetValue(name, out var list))
                    _externalBindings[className][name] = list = new();
                list.Add(new ExternalMethod(wireName, name, method));
            }

            // Expose public fields + properties (everything) for binding
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (field.IsSpecialName) continue;
                var fieldAttr = field.GetCustomAttribute<FieldBindingAttribute>();
                string name = fieldAttr?.Name ?? field.Name;
                if (!_externalFields[className].ContainsKey(name))
                    _externalFields[className][name] = new ExternalField(wireName, name, field, field.FieldType, false);
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (prop.IsSpecialName) continue;
                var propAttr = prop.GetCustomAttribute<FieldBindingAttribute>();
                string name = propAttr?.Name ?? prop.Name;
                // Accessor check: need at least getter or setter
                if (prop.GetMethod == null && prop.SetMethod == null) continue;
                if (!_externalFields[className].ContainsKey(name))
                    _externalFields[className][name] = new ExternalField(wireName, name, prop, prop.PropertyType, true);
            }

            // Register attribute constructors (for any arity C# attribute exposure)
            if (type.IsSubclassOf(typeof(Attribute)) || type == typeof(Attribute))
            {
                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ToList();
                if (ctors.Count > 0)
                {
                    string shortAttrName = type.Name.EndsWith("Attribute", StringComparison.Ordinal) ? type.Name[..^9] : type.Name;
                    // register under both full CLR name and short attribute name for lookup
                    if (!_externalAttributeCtors.ContainsKey(shortAttrName))
                        _externalAttributeCtors[shortAttrName] = new();
                    // dedup by signature
                    foreach (var c in ctors)
                        if (!_externalAttributeCtors[shortAttrName].Any(existing => existing.GetParameters().Length == c.GetParameters().Length && existing.GetParameters().Select(p => p.ParameterType) .SequenceEqual(c.GetParameters().Select(p=>p.ParameterType))))
                            _externalAttributeCtors[shortAttrName].Add(c);

                    string fullKey = type.FullName ?? className;
                    if (!_externalAttributeCtors.ContainsKey(fullKey))
                        _externalAttributeCtors[fullKey] = new();
                    foreach (var c in ctors)
                        if (!_externalAttributeCtors[fullKey].Any(existing => existing.GetParameters().Length == c.GetParameters().Length))
                            _externalAttributeCtors[fullKey].Add(c);

                    // Also register under wire name if different
                    if (wireName != shortAttrName && wireName != fullKey)
                    {
                        if (!_externalAttributeCtors.ContainsKey(wireName))
                            _externalAttributeCtors[wireName] = new();
                        foreach (var c in ctors)
                            if (!_externalAttributeCtors[wireName].Any(existing => existing.GetParameters().Length == c.GetParameters().Length))
                                _externalAttributeCtors[wireName].Add(c);
                    }
                }
            }
            // Also register attribute ctors for non-Attribute types used as attribute source via ClrImport scanning
            // (generic attributes like MyAttr<T> may be discovered via RegisterAssembly scanning all types)
            // To support <SomeCSharpAttr> where SomeCSharpAttr is a ClassBinding type not subclassing Attribute,
            // we still store its ctors if someone marks it with [AttributeUsage] — the SymbolTable fallback will handle it.
            if (type.GetCustomAttribute<AttributeUsageAttribute>() != null)
            {
                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ToList();
                if (ctors.Count > 0)
                {
                    string key = type.Name.EndsWith("Attribute", StringComparison.Ordinal) ? type.Name[..^9] : type.Name;
                    if (!_externalAttributeCtors.ContainsKey(key))
                        _externalAttributeCtors[key] = ctors;
                }
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

            // IL wins union: try IL contract first, fallback to host bindings
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
                // For shadowed contracts, fall through to host if IL doesn't have the method
                if (IsShadowedModule(className))
                {
                    string? resolved = ResolveModuleName(GetShadowWireName(className) ?? className);
                    if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var slist) && slist.Count > 0)
                    {
                        method = slist[0];
                        return true;
                    }
                }
                // Non-shadowed user contract that doesn't have the method -> host check via className
                // (existing behavior already falls through below)
            }

            string? r = ResolveModuleName(className);
            if (r != null && _externalBindings[r].TryGetValue(methodName, out var list) && list.Count > 0)
            {
                method = list[0];
                return true;
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

            // IL wins union
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
                if (IsShadowedModule(className))
                {
                    string? resolved = ResolveModuleName(GetShadowWireName(className) ?? className);
                    if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var slist) && slist.Count > 0)
                    {
                        var m = slist.FirstOrDefault(x => x.Info.GetParameters().Length == argCount);
                        method = m ?? slist[0];
                        return true;
                    }
                }
            }

            string? resolved2 = ResolveModuleName(className);
            if (resolved2 != null && _externalBindings[resolved2].TryGetValue(methodName, out var list) && list.Count > 0)
            {
                var match = list.FirstOrDefault(m => m.Info.GetParameters().Length == argCount);
                method = match ?? list[0];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a method on the HOST binding only (bypasses the IL-wins
        /// union). Used by <c>host.Method(...)</c> — which must reach the C#
        /// binding even when the IL shadow contract declares the same name.
        /// </summary>
        public bool TryResolveHostMethod(string wireName, string methodName, int argCount, out ExternalMethod? method)
        {
            method = null;
            string? resolved = ResolveModuleName(wireName);
            if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list) && list.Count > 0)
            {
                var match = list.FirstOrDefault(m => m.Info.GetParameters().Length == argCount);
                method = match ?? list[0];
                return true;
            }
            return false;
        }

        /// <summary>All overloads of a method name on a module, or an empty list when unknown.</summary>
        public IReadOnlyList<ExternalMethod> GetMethodOverloads(string className, string methodName)
        {
            // For shadowed modules, return host overloads (IL has no overloads concept)
            if (IsShadowedModule(className))
            {
                string? sw = GetShadowWireName(className);
                if (sw != null)
                {
                    string? r2 = ResolveModuleName(sw);
                    if (r2 != null && _externalBindings[r2].TryGetValue(methodName, out var slist))
                        return slist;
                }
            }
            string? resolved = ResolveModuleName(className);
            if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list))
                return list;
            return Array.Empty<ExternalMethod>();
        }

        /// <summary>All overloads merged: IL presence + host overloads.</summary>
        public IReadOnlyList<object> GetMethodOverloadsMerged(string className, string methodName)
        {
            var result = new List<object>();
            if (_userContracts.TryGetValue(className, out var contract))
            {
                foreach (var m in contract.Members)
                    if (m is FunctionDeclaration f && f.Name == methodName)
                        result.Add(f);
            }
            // add host overloads if shadowed or no IL match
            string? hostKey = IsShadowedModule(className) ? GetShadowWireName(className) : className;
            if (hostKey != null)
            {
                string? resolved = ResolveModuleName(hostKey);
                if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list))
                    foreach (var e in list) result.Add(e);
            }
            else
            {
                string? resolved = ResolveModuleName(className);
                if (resolved != null && _externalBindings[resolved].TryGetValue(methodName, out var list2))
                    foreach (var e in list2) result.Add(e);
            }
            return result;
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
            return names.Distinct().ToList();
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

        // ── Field / attribute / shadow helpers ───────────────────────

        public bool TryGetField(string className, string fieldName, out ExternalField? field)
        {
            field = null;
            string? hostKey = IsShadowedModule(className) ? GetShadowWireName(className) : className;
            if (hostKey != null)
            {
                string? r = ResolveModuleName(hostKey);
                if (r != null && _externalFields.TryGetValue(r, out var map) && map.TryGetValue(fieldName, out field))
                    return true;
            }
            string? resolved = ResolveModuleName(className);
            if (resolved != null && _externalFields.TryGetValue(resolved, out var map2) && map2.TryGetValue(fieldName, out field))
                return true;
            return false;
        }

        public IEnumerable<ExternalField> GetExternalFields(string className)
        {
            string? hostKey = IsShadowedModule(className) ? GetShadowWireName(className) : className;
            if (hostKey != null)
            {
                string? r = ResolveModuleName(hostKey);
                if (r != null && _externalFields.TryGetValue(r, out var map))
                    return map.Values;
            }
            string? resolved = ResolveModuleName(className);
            return resolved != null && _externalFields.TryGetValue(resolved, out var map2)
                ? map2.Values : Enumerable.Empty<ExternalField>();
        }

        public bool IsExternalField(string className, string fieldName)
            => TryGetField(className, fieldName, out _);

        public IReadOnlyList<ConstructorInfo> GetExternalAttributeCtors(string attributeName)
        {
            if (_externalAttributeCtors.TryGetValue(attributeName, out var list))
                return list;
            // try stripping Attribute suffix
            string shortName = attributeName.EndsWith("Attribute", StringComparison.Ordinal) ? attributeName[..^9] : attributeName;
            if (_externalAttributeCtors.TryGetValue(shortName, out var list2))
                return list2;
            // try generic name without args: MyAttr<int> -> MyAttr
            int tickIdx = attributeName.IndexOf('<');
            if (tickIdx > 0)
            {
                string baseName = attributeName[..tickIdx];
                if (_externalAttributeCtors.TryGetValue(baseName, out var list3))
                    return list3;
                string shortBase = baseName.EndsWith("Attribute", StringComparison.Ordinal) ? baseName[..^9] : baseName;
                if (_externalAttributeCtors.TryGetValue(shortBase, out var list4))
                    return list4;
            }
            return Array.Empty<ConstructorInfo>();
        }

        public bool IsExternalAttribute(string name)
            => GetExternalAttributeCtors(name).Count > 0;

        // ── ShadowBinding helpers ────────────────────────────────────

        public bool IsShadowedModule(string contractName)
        {
            if (_userContracts.TryGetValue(contractName, out var c))
                return c.IsShadowed && c.ShadowTarget != null;
            return false;
        }

        public string? GetShadowWireName(string contractName)
        {
            if (_userContracts.TryGetValue(contractName, out var c))
                return c.ShadowTarget;
            return null;
        }

        public string? GetExternalBindingWireName(string className)
        {
            string? r = ResolveModuleName(className);
            if (r != null && _externalBindings.TryGetValue(r, out var map) && map.Count > 0)
            {
                // any method's wire name
                foreach (var lst in map.Values)
                    if (lst.Count > 0) return lst[0].ClassName;
            }
            if (r != null && _externalFields.TryGetValue(r, out var fmap) && fmap.Count > 0)
                return fmap.Values.First().ClassName;
            return r;
        }

        public IEnumerable<string> GetShadowedContracts()
            => _userContracts.Values.Where(c => c.IsShadowed).Select(c => c.Name).Distinct();

        /// <summary>Builds shadow links for contracts that share name with host wire names (auto-shadow) and explicit targets.</summary>
        public void BuildShadowMap()
        {
            foreach (var kv in _userContracts.ToList())
            {
                var contract = kv.Value;
                if (contract.IsShadowed && contract.ShadowTarget != null) continue; // explicit already
                // auto-shadow if contract name matches a host wire short name
                // check wire names of external bindings
                foreach (var extKey in _externalBindings.Keys)
                {
                    string wireShort = extKey.Contains('.') ? extKey[(extKey.LastIndexOf('.')+1)..] : extKey;
                    string hostWire = GetExternalBindingWireName(extKey) ?? wireShort;
                    if (string.Equals(contract.Name, hostWire, StringComparison.Ordinal) || string.Equals(contract.FullName, hostWire, StringComparison.Ordinal))
                    {
                        contract.IsShadowed = true;
                        contract.ShadowTarget = extKey; // key for ResolveModuleName
                        break;
                    }
                    if (string.Equals(contract.Name, wireShort, StringComparison.Ordinal))
                    {
                        // also consider short name match via imported namespaces
                        contract.IsShadowed = true;
                        contract.ShadowTarget = extKey;
                        break;
                    }
                }
            }
        }

        /// <summary>All members merged for a shadowed type (IL first, host fallback dedup).</summary>
        public IEnumerable<string> GetMergedMemberNames(string className)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (_userContracts.TryGetValue(className, out var contract))
            {
                foreach (var m in contract.Members)
                    if (m is FunctionDeclaration f && seen.Add(f.Name))
                        yield return f.Name;
                foreach (var fld in contract.Fields)
                    if (seen.Add(fld.Name))
                        yield return fld.Name;
            }
            string? hostKey = IsShadowedModule(className) ? GetShadowWireName(className) : className;
            if (hostKey != null)
            {
                string? r = ResolveModuleName(hostKey);
                if (r != null)
                {
                    if (_externalBindings.TryGetValue(r, out var map))
                        foreach (var k in map.Keys) if (seen.Add(k)) yield return k;
                    if (_externalFields.TryGetValue(r, out var fmap))
                        foreach (var k in fmap.Keys) if (seen.Add(k)) yield return k;
                }
            }
        }
    }
}
