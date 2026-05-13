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
        private readonly Dictionary<string, Dictionary<string, ExternalMethod>> _externalBindings = new();
        private readonly Dictionary<string, ContractDeclaration> _userContracts = new();

        public void RegisterAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                var classAttr = type.GetCustomAttribute<ClassBindingAttribute>();
                if (classAttr == null) continue;

                if (!_externalBindings.ContainsKey(classAttr.Name))
                    _externalBindings[classAttr.Name] = new();

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                {
                    var methodAttr = method.GetCustomAttribute<MethodBindingAttribute>();
                    if (methodAttr == null) continue;

                    string name = methodAttr.Name ?? method.Name;
                    _externalBindings[classAttr.Name][name] = new ExternalMethod(classAttr.Name, name, method);
                }
            }
        }

        public void RegisterUserContract(ContractDeclaration contract)
        {
            _userContracts[contract.Name] = contract;
        }

        public bool TryGetMethod(string className, string methodName, out object? method)
        {
            method = null;
            if (_externalBindings.TryGetValue(className, out var externalMethods))
            {
                if (externalMethods.TryGetValue(methodName, out var m))
                {
                    method = m;
                    return true;
                }
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

        public IEnumerable<string> GetBoundClasses() => _externalBindings.Keys.Concat(_userContracts.Keys);
    }
}
