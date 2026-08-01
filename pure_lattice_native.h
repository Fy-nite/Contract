#include <cstdint>
#include <string>
#include <vector>
#include <variant>
#include <unordered_map>


/// this file is meant for testing the C++ FCC compiler. 
/// use at your own risk.


namespace ObjectIR
{
    // -----------------------------------------------------------------------
    // Array — static array helper
    // -----------------------------------------------------------------------
    class Array
    {
        public:
            static int32_t Length(void* arr);
            static void* Get(void* arr, int32_t index);
            static void Set(void* arr, int32_t index, void* value);
    };

    // -----------------------------------------------------------------------
    // Convert — type conversion helpers
    // -----------------------------------------------------------------------
    class Convert
    {
        public:
            static int32_t ToInt32(const std::string& value);
            static std::string ToString(int32_t value);
            static std::string ToStringF(float value);
            static std::string ToStringB(bool value);
            static float ToFloat32(const std::string& value);
            static int32_t ToInt32F(float value);
            static float ToFloat32I(int32_t value);
            static bool ToBool(const std::string& value);
    };

    // -----------------------------------------------------------------------
    // Debug — assertions
    // -----------------------------------------------------------------------
    class Debug
    {
        public:
            static void Assert(bool condition, const std::string& message);
    };

    // -----------------------------------------------------------------------
    // Dict — dynamic dictionary (string-keyed object map)
    // -----------------------------------------------------------------------
    class Dict
    {
        public:
            static void* Create();
            static void Set(void* dict, void* key, void* value);
            static void* Get(void* dict, void* key);
            static bool ContainsKey(void* dict, void* key);
            static bool Remove(void* dict, void* key);
            static void* Keys(void* dict);
            static int32_t Count(void* dict);
    };

    // -----------------------------------------------------------------------
    // Environment — OS environment access
    // -----------------------------------------------------------------------
    class Environment
    {
        public:
            static std::string GetEnv(const std::string& name);
            static void Exit(int32_t code);
    };

    // -----------------------------------------------------------------------
    // File — file system I/O
    // -----------------------------------------------------------------------
    class File
    {
        public:
            static std::string ReadAllText(const std::string& path);
            static void WriteAllText(const std::string& path, const std::string& contents);
            static bool Exists(const std::string& path);
            static void* ReadAllLines(const std::string& path);
            static void Copy(const std::string& src, const std::string& dst);
            static void Move(const std::string& src, const std::string& dst);
            static void Delete(const std::string& path);
    };

    // -----------------------------------------------------------------------
    // GC — garbage collection
    // -----------------------------------------------------------------------
    class GC
    {
        public:
            static void Collect();
    };

    // -----------------------------------------------------------------------
    // IO — console input / output
    // -----------------------------------------------------------------------
    class IO
    {
        public: 
            static void Println(const std::string value);
            static void Print(const std::string value);
            static std::string Readln();
    };

    // -----------------------------------------------------------------------
    // List — dynamic object list
    // -----------------------------------------------------------------------
    class List
    {
        public:
            static void* Create();
            static void Add(void* list, void* item);
            static void* Get(void* list, int32_t index);
            static void Set(void* list, int32_t index, void* item);
            static int32_t Count(void* list);
            static void RemoveAt(void* list, int32_t index);
    };

    // -----------------------------------------------------------------------
    // Math — numeric operations
    // -----------------------------------------------------------------------
    class Math
    {
        public:
            static float Sqrt(float value);
            static int32_t Abs(int32_t value);
            static float AbsF(float value);
            static int32_t Min(int32_t a, int32_t b);
            static float MinF(float a, float b);
            static int32_t Max(int32_t a, int32_t b);
            static float MaxF(float a, float b);
            static float Pow(float x, float y);
            static int32_t Floor(float value);
            static int32_t Ceiling(float value);
            static float Sin(float value);
            static float Cos(float value);
            static float Tan(float value);
            static float Log(float value);
            static float Log10(float value);
            static float Exp(float value);
            static int32_t Round(float value);
    };

    // -----------------------------------------------------------------------
    // Random — pseudo-random number generation
    // -----------------------------------------------------------------------
    class Random
    {
        public:
            static int32_t NextInt(int32_t max);
            static float NextFloat();
    };

    // -----------------------------------------------------------------------
    // String — static string operations
    // -----------------------------------------------------------------------
    class String
    {
        public:
            static int32_t Length(const std::string& str);
            static std::string Concat(const std::string& a, const std::string& b);
            static std::string Substring(const std::string& str, int32_t start, int32_t length);
            static int32_t IndexOf(const std::string& str, const std::string& sub);
            static bool StartsWith(const std::string& str, const std::string& prefix);
            static bool EndsWith(const std::string& str, const std::string& suffix);
            static std::string Trim(const std::string& str);
            static std::string ToUpper(const std::string& str);
            static std::string ToLower(const std::string& str);
            static void* Split(const std::string& str, const std::string& separator);
            static std::string Replace(const std::string& str, const std::string& old, const std::string& new_);
    };

    // -----------------------------------------------------------------------
    // Time — timestamps and formatting
    // -----------------------------------------------------------------------
    class Time
    {
        public:
            static int64_t Now();
            static std::string Format(int64_t timestamp, const std::string& format);
    };

    // -----------------------------------------------------------------------
    // Thread — threading / sleeping
    // -----------------------------------------------------------------------
    class Thread
    {
        public:
            static void Spawn(void* delegate_);
            static void Sleep(int32_t ms);
    };

    // -----------------------------------------------------------------------
    // Action — a delegate that takes no arguments and returns void
    // -----------------------------------------------------------------------
    class Action
    {
        public:
            // constructor(object instance, string methodName)
            void* instance;
            std::string methodName;
            void Invoke();
    };

    // -----------------------------------------------------------------------
    // Func — a delegate that takes no arguments and returns an object
    // -----------------------------------------------------------------------
    class Func
    {
        public:
            // constructor(object instance, string methodName)
            void* instance;
            std::string methodName;
            void* Invoke();
    };

    // -----------------------------------------------------------------------
    // Delegate — a generic delegate with target + methodName fields
    // -----------------------------------------------------------------------
    class Delegate
    {
        public:
            // constructor(object target, string methodName)
            void* Target;
            std::string MethodName;
    };

    // -----------------------------------------------------------------------
    // Malloc — heap allocation and memory inspection
    // -----------------------------------------------------------------------
    class Malloc
    {
        public:
            static void* Alloc(const std::string typeName);
            static int32_t GetUsedMemory();
            static int32_t GetFreeMemory();
            static void Reset();
    };
}
// ---------------------------------------------------------------------------
// 7. Bytecode interpreter helper — decode a CompactInstr operand based on
//    the opcode. Useful when building a disassembler or test harness.
// ---------------------------------------------------------------------------

enum class TypeKind : uint8_t {
    Void,
    Bool,
    Int32,
    Float32,
    String,
    Object,   // managed object / class instance
};

struct Value {
    TypeKind kind = TypeKind::Void;
    std::variant<std::monostate, bool, int32_t, float, std::string, void*> data;

    Value() : data(std::monostate{}) {}
    static Value Void() { return Value{}; }
    static Value FromBool(bool v)   { Value val; val.kind = TypeKind::Bool;    val.data = v; return val; }
    static Value FromInt(int32_t v) { Value val; val.kind = TypeKind::Int32;   val.data = v; return val; }
    static Value FromFloat(float v) { Value val; val.kind = TypeKind::Float32; val.data = v; return val; }
    static Value FromString(const std::string& v) { Value val; val.kind = TypeKind::String; val.data = v; return val; }
    static Value FromObject(void* v) { Value val; val.kind = TypeKind::Object; val.data = v; return val; }
};

// ---------------------------------------------------------------------------
// 2. ObjectIR opcodes (mirrors ObjectIR.Core.Ast.OpCode)
// ---------------------------------------------------------------------------

enum class OpCode : uint16_t {
    // Load instructions
    Ldarg   = 0,
    Ldloc   = 1,
    Ldfld   = 2,
    Ldsfld  = 3,
    Ldelem  = 4,
    Ldlen   = 5,
    Ldnull  = 6,
    LdcI4   = 7,
    LdcI8   = 8,
    LdcR4   = 9,
    LdcR8   = 10,
    Ldstr   = 11,

    // Store instructions
    Starg   = 12,
    Stloc   = 13,
    Stfld   = 14,
    Stsfld  = 15,
    Stelem  = 16,

    // Arithmetic
    Add     = 17,
    Sub     = 18,
    Mul     = 19,
    Div     = 20,
    Rem     = 21,
    Neg     = 22,
    And     = 23,
    Or      = 24,
    Xor     = 25,
    Not     = 26,
    Shl     = 27,
    Shr     = 28,

    // Comparison
    Ceq     = 29,
    Cne     = 30,
    Cgt     = 31,
    CgtUn   = 32,
    CgeUn   = 33,
    Clt     = 34,

    // Control flow
    Br      = 35,
    Brtrue  = 36,
    Brfalse = 37,
    Beq     = 38,
    Bne     = 39,
    Bgt     = 40,
    Blt     = 41,
    Ret     = 42,

    // Calls
    Call    = 43,
    Callvirt= 44,
    Calli   = 45,
    Newobj  = 46,

    // Object operations
    Newarr  = 47,
    Castclass = 48,
    Isinst  = 49,
    Box     = 50,
    Unbox   = 51,

    // Stack manipulation
    Dup     = 52,
    Pop     = 53,

    // Conversions
    ConvI4  = 54,
    ConvI8  = 55,
    ConvR4  = 56,
    ConvR8  = 57,
    ConvU4  = 58,
    ConvU8  = 59,

    // Structured control flow (high-level)
    If      = 60,
    While   = 61,
    For     = 62,
    Switch  = 63,
    Try     = 64,
    Break   = 65,
    Continue= 66,
    Throw   = 67,
};

// ---------------------------------------------------------------------------
// 3. Compact instruction (matches CompactInstr in CompiledMethod.cs)
// ---------------------------------------------------------------------------

struct CompactInstr {
    OpCode opcode;
    int32_t operand = 0;
};

// ---------------------------------------------------------------------------
// 4. Compiled method (matches CompiledMethod in CompiledMethod.cs)
// ---------------------------------------------------------------------------

struct CallTarget {
    std::string qualified_name;   // e.g. "IO.Println"
    std::vector<TypeKind> param_types;
    TypeKind return_type = TypeKind::Void;
};

struct NewObjTarget {
    std::string type_name;
    std::string ctor_signature;
};

struct CompiledMethod {
    std::string name;
    int local_count  = 0;
    int arg_count    = 0;
    bool returns_value = false;

    std::vector<CompactInstr> code;
    std::vector<std::string>  string_table;
    std::vector<float>        float_table;
    std::vector<std::string>  local_names;
    std::vector<std::string>  arg_names;
    std::vector<CallTarget>   call_targets;
    std::vector<NewObjTarget> newobj_targets;
};
inline const char* opcode_name(OpCode op) {
    switch (op) {
        case OpCode::Ldarg:      return "ldarg";
        case OpCode::Ldloc:      return "ldloc";
        case OpCode::Ldfld:      return "ldfld";
        case OpCode::Ldsfld:     return "ldsfld";
        case OpCode::Ldelem:     return "ldelem";
        case OpCode::Ldlen:      return "ldlen";
        case OpCode::Ldnull:     return "ldnull";
        case OpCode::LdcI4:      return "ldc.i4";
        case OpCode::LdcI8:      return "ldc.i8";
        case OpCode::LdcR4:      return "ldc.r4";
        case OpCode::LdcR8:      return "ldc.r8";
        case OpCode::Ldstr:      return "ldstr";
        case OpCode::Starg:      return "starg";
        case OpCode::Stloc:      return "stloc";
        case OpCode::Stfld:      return "stfld";
        case OpCode::Stsfld:     return "stsfld";
        case OpCode::Stelem:     return "stelem";
        case OpCode::Add:        return "add";
        case OpCode::Sub:        return "sub";
        case OpCode::Mul:        return "mul";
        case OpCode::Div:        return "div";
        case OpCode::Rem:        return "rem";
        case OpCode::Neg:        return "neg";
        case OpCode::And:        return "and";
        case OpCode::Or:         return "or";
        case OpCode::Xor:        return "xor";
        case OpCode::Not:        return "not";
        case OpCode::Shl:        return "shl";
        case OpCode::Shr:        return "shr";
        case OpCode::Ceq:        return "ceq";
        case OpCode::Cne:        return "cne";
        case OpCode::Cgt:        return "cgt";
        case OpCode::CgtUn:      return "cgt.un";
        case OpCode::CgeUn:      return "cge.un";
        case OpCode::Clt:        return "clt";
        case OpCode::Br:         return "br";
        case OpCode::Brtrue:     return "brtrue";
        case OpCode::Brfalse:    return "brfalse";
        case OpCode::Beq:        return "beq";
        case OpCode::Bne:        return "bne";
        case OpCode::Bgt:        return "bgt";
        case OpCode::Blt:        return "blt";
        case OpCode::Ret:        return "ret";
        case OpCode::Call:       return "call";
        case OpCode::Callvirt:   return "callvirt";
        case OpCode::Calli:      return "calli";
        case OpCode::Newobj:     return "newobj";
        case OpCode::Newarr:     return "newarr";
        case OpCode::Castclass:  return "castclass";
        case OpCode::Isinst:     return "isinst";
        case OpCode::Box:        return "box";
        case OpCode::Unbox:      return "unbox";
        case OpCode::Dup:        return "dup";
        case OpCode::Pop:        return "pop";
        case OpCode::ConvI4:     return "conv.i4";
        case OpCode::ConvI8:     return "conv.i8";
        case OpCode::ConvR4:     return "conv.r4";
        case OpCode::ConvR8:     return "conv.r8";
        case OpCode::ConvU4:     return "conv.u4";
        case OpCode::ConvU8:     return "conv.u8";
        case OpCode::If:         return "if";
        case OpCode::While:      return "while";
        case OpCode::For:        return "for";
        case OpCode::Switch:     return "switch";
        case OpCode::Try:        return "try";
        case OpCode::Break:      return "break";
        case OpCode::Continue:   return "continue";
        case OpCode::Throw:      return "throw";
    }
    return "unknown";
}

// Returns true if the opcode pops exactly one value and pushes nothing
// (i.e. it is a side-effect-only instruction like Pop, Stloc, Starg,
// Stfld, etc.).
inline bool opcode_is_side_effect_only(OpCode op) {
    switch (op) {
        case OpCode::Stloc:
        case OpCode::Starg:
        case OpCode::Stfld:
        case OpCode::Stsfld:
        case OpCode::Stelem:
        case OpCode::Pop:
            return true;
        default:
            return false;
    }
}

// Returns true if the opcode is a branch/jump instruction.
inline bool opcode_is_branch(OpCode op) {
    switch (op) {
        case OpCode::Br:
        case OpCode::Brtrue:
        case OpCode::Brfalse:
        case OpCode::Beq:
        case OpCode::Bne:
        case OpCode::Bgt:
        case OpCode::Blt:
            return true;
        default:
            return false;
    }
}

// Returns true if the opcode is a comparison (pushes a bool result).
inline bool opcode_is_comparison(OpCode op) {
    switch (op) {
        case OpCode::Ceq:
        case OpCode::Cne:
        case OpCode::Cgt:
        case OpCode::CgtUn:
        case OpCode::CgeUn:
        case OpCode::Clt:
            return true;
        default:
            return false;
    }
}