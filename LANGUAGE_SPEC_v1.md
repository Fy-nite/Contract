# Contract language — Version 1 (v1) specification

Status: draft — this document defines the intended v1 language surface, a
formal grammar (EBNF), lexical rules, the intermediate language (IL) /
bytecode format emitted by the reference compiler, and the VM semantics.
The goal is a stable reference for tooling and a canonical compatibility
target for future implementations.

## Goals for v1

- Small, orthogonal feature set suitable for examples and teaching.
- Clear, deterministic semantics and small predictable bytecode.
- A simple calling convention to support recursion and local variables.
- Designed to be easy to parse with a hand-written recursive-descent parser.

## Overview

- Source files: UTF-8 text, extension `.ct`.
- Top-level constructs: Contracts and function declarations.
- Basic types: integers and strings (no explicit type system in v1).
- Control flow: if/else, while, switch/case, return.
- Expressions: literals, identifiers, member access, function calls, binary ops.

## Lexical grammar

Tokens (all whitespace between tokens is ignored except inside strings):

- IDENTIFIER: [A-Za-z_][A-Za-z0-9_]* (dotted qualified names allowed via '.' between identifiers)
- INT: [0-9]+ (decimal only)
- STRING: double quoted, supports escapes like `\\n`, `\\"`, `\\\\`.
- Keywords: `Contract`, `fn`, `switch`, `case`, `else`, `if`, `while`, `return`, `var`
- Symbols: `(`, `)`, `{`, `}`, `;`, `:`, `,`, `.`, `+`, `-`, `<`, `<=`, `==`, `=` (assignment),
  `->` (future use)

Comments: `//` to end-of-line.

All tokens are case-sensitive.

## EBNF grammar (v1)
```
Start ::= TopLevel*

TopLevel ::= ContractDecl | FunctionDecl

ContractDecl ::= 'Contract' IDENTIFIER '{' TopLevel* '}'

FunctionDecl ::= 'fn' IDENTIFIER '(' ParamList? ')' Block

ParamList ::= Param (',' Param)*
Param ::= IDENTIFIER (':' IDENTIFIER)?

Block ::= '{' Statement* '}'

Statement ::= ExprStatement | VarDecl | IfStmt | WhileStmt | SwitchStmt | ReturnStmt

ExprStatement ::= Expression ';'
VarDecl ::= 'var' IDENTIFIER (':' IDENTIFIER)? ('=' Expression)? ';'
IfStmt ::= 'if' '(' Expression ')' Block ('else' ':'? (Block | Statement))?
WhileStmt ::= 'while' '(' Expression ')' Block
SwitchStmt ::= 'switch' Expression '{' ( 'case' INT ':' Statement* )* ('else' ':' Statement* )? '}'
ReturnStmt ::= 'return' Expression? ';'

Expression ::= Assignment
Assignment ::= Equality ( '=' Assignment )?
Equality ::= Relational ( ('==' ) Relational )*
Relational ::= Additive ( ('<' | '<=') Additive )*
Additive ::= Term ( ('+' | '-') Term )*
Term ::= Primary
Primary ::= INT | STRING | IDENTIFIER ('.' IDENTIFIER)* ( '(' ArgList? ')' )?
ArgList ::= Expression (',' Expression)*
```
Notes:
- The grammar intentionally keeps operator precedence simple: additive > relational > equality.
- Member access `A.B` is parsed as a dotted identifier; `A.B(...)` is a call on a qualified name.

## Semantics

- No static typing in v1; runtime values are integers or references to strings.
- Function call semantics: arguments are evaluated left-to-right and pushed on the caller's operand stack. The caller then invokes the callee which maps arguments to its locals in left-to-right order.
- Variable scoping: functions have locals; `var` declares a local in the enclosing function or block (implementation-defined in v1 — recommended: function-level locals only).
- `switch` uses integer case values; evaluation matches the numeric case and executes the associated statements; `else` acts as default.

## IL / Bytecode format (CIL1)

File layout (binary):

- Header: 4 bytes ASCII `CIL1` + u32 version (1)
- Constants area: sequence of null-terminated UTF-8 strings
- Method table: records with method name offset (into constants), arg_count (u16), local_count (u16), code_offset (u32), code_length (u32)
- Code blob: concatenated bytecode for all methods

Instruction set (one-byte opcodes unless followed by immediates):

- I_NOP (0x00)
- I_LDC_I4 (0x01) <i32 little-endian> — push int32 constant
- I_LDC_STR (0x02) <u32 const_offset> — push referenced constant string address
- I_PRINT_CONST (0x03) — pop and print const/string
- I_PRINT_INT (0x04) — pop and print int
- I_LOAD_LOCAL (0x10) <u8 index>
- I_STORE_LOCAL (0x11) <u8 index>
- I_ADD (0x20), I_SUB (0x21), I_MUL (0x22), I_DIV (0x23)
- I_LT (0x30), I_LE (0x31), I_EQ (0x32)
- I_JZ (0x40) <u32 offset> — jump if top-of-stack zero
- I_JMP (0x41) <u32 offset>
- I_CALL (0x50) <u32 method_index> — call by method table index
- I_RET (0x51)

Notes:
- Offsets in jump immediates are absolute offsets within the code blob.
- The method table index used by I_CALL is a 32-bit index into the method table.

## VM semantics

- The VM maintains a call stack of frames; each frame contains:
  - return_ip (offset into code blob),
  - method_index,
  - locals array (sized per method),
  - operand stack (growable)
- On I_CALL, VM pushes a new frame, copies arguments from the caller's stack into the callee's locals (left-to-right), and transfers control to the callee's code_offset.
- On I_RET, VM pops the frame and resumes the caller at return_ip. If the language later introduces return values, convention will be to push the result onto the caller's operand stack before returning.

Error handling

- The VM signals runtime errors for:
  - stack underflow/overflow (implementation-defined overflow limit),
  - invalid local index access,
  - invalid instruction,
  - division by zero.
- The reference implementation prints an error and aborts the current program.

Versioning and compatibility

- The header version number is used for format evolution. Tools must refuse to load unknown versions.
- Language v1 is intentionally conservative: additions are allowed later but removal/change of opcode semantics is forbidden for backward compatibility.

Conformance notes (relationship to this repository)

- The reference C compiler in `src/` emits the CIL1 format described above.
- The `src/runtime.c` implements the VM semantics above.
- The LSP server uses the reference compiler to emit diagnostics and symbols.

Examples

- A simple factorial function in Contract syntax and the approximate bytecode emitted by the reference compiler.

Contract source:

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

Corresponding (conceptual) IL:

- method table: Math.fact arg_count=1 local_count=2 code_offset=0 code_length=...
- code at offset 0:
  I_LOAD_LOCAL 0    ; n
  I_LDC_I4 1
  I_LT
  I_JZ <after_loop>
  I_LOAD_LOCAL 1    ; acc
  I_LOAD_LOCAL 0
  I_MUL
  I_STORE_LOCAL 1
  I_LOAD_LOCAL 0
  I_LDC_I4 1
  I_SUB
  I_STORE_LOCAL 0
  I_JMP <loop_top>
  <after_loop>:
  I_LOAD_LOCAL 1
  I_RET

Future extensions (non-normative)

- Add a small type system with optional annotations.
- Add first-class strings and string ops in the IL.
- Add optimized opcodes for small integer constants (I_LDC_0..I_LDC_7).

Feedback and process

This spec is intended as the canonical v1. If you'd like, I can:

- convert the EBNF to an ANTLR grammar or a PEG for automated parsing,
- generate a test-suite of small example programs and expected diagnostics/IL,
- expand the IL to include debug info (line/col mapping) for improved tooling.
