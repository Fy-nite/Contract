# Contract Language — Implementation Specification (v1-impl)

This document describes the features currently supported by the Contract compiler as of May 2026. It serves as a guide for writing valid Contract programs and understanding the compiler's behavior.

## Core Language Features

### 1. Structure
Contract source files (`.ct`) consist of top-level **Contract** declarations and **Function** declarations.

```ct
Contract MyContract {
    fn internalFunc(x) { ... }
}

fn globalFunc() { ... }
```

### 2. Modifiers
The compiler supports access modifiers and static markers for function declarations:
- **Modifiers:** `public`, `private`, `protected`, `internal`
- **Static:** `static`

```ct
Contract Program {
    static fn Main() { ... }
    public fn API() { ... }
}
```

### 3. Variables
Variables are declared using `var` or `let`. In v1, types are optional and the language is dynamically typed at runtime.

```ct
var x = 5;
let message = "hello";
var y: int = 10; // Type annotation (parsed but ignored in v1-impl)
```

### 4. Control Flow
The language supports standard imperative control flow:
- **If/Else:** Standard conditional branching.
- **While:** Standard loops.
- **Switch:** Integer-based branching with `case` and `else` (default).

```ct
switch (x) {
    case 1: print("one");
    case 2: print("two");
    else: print("unknown");
}
```

### 5. Expressions
- **Arithmetic:** `+`, `-`, `*`, `/`
- **Comparison:** `==`, `!=`, `<`, `<=`, `>`, `>=`
- **Assignment:** `=`
- **Member Access:** `Object.Property` or `Object.Method()`
- **Function Calls:** `func(arg1, arg2)`

## Rich Diagnostics
The compiler implements a Rust-style diagnostic system to help identify and fix errors quickly.

**Example Error Output:**
```ansi
error: Expected ';' after expression
  --> HelloWorld.ct:14:19
   |
14 |         IO.Println("hello world")
   |                   ^
```

## Complete Examples

### Factorial (Math.ct)
```ct
Contract Math {
    fn fact(n) {
        var acc = 1;
        while (n > 1) {
            acc = acc * n;
            n = n - 1;
        }
        return acc;
    }
}
```

### Hello World (HelloWorld.ct)
```ct
Contract Program {
    static fn Main() {
        IO.Println("hello world");
        IO.Println(fact(5));
    }

    fn fact(n) {
        var acc = 1;
        while(n > 1) {
            acc = acc * n; 
            n = n - 1; 
        } 
        return acc; 
    }
}
```

## Compilation Process
To compile a file and see the AST/Diagnostics:
```bash
ccl path/to/file.ct
```
The compiler will output:
1.  **Tokens:** The stream of lexical tokens.
2.  **AST:** A hierarchical view of the parsed program.
3.  **Diagnostics:** Rich error messages if applicable.
4.  **Bytecode:** Generates a `.cil` file (Common Intermediate Language) for the Contract VM.
