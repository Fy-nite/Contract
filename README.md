# ContractIR

A programming language and compiler infrastructure for the Contract language.

## Overview

Contract is a statically-typed language that compiles to CIL1 (Contract Intermediate Language v1) bytecode. It features a clean, modern syntax with contracts, functions, structs, and functional programming constructs.

## Features

- **Contracts**: Organizational units similar to namespaces/classes
- **Functions**: First-class functions with optional type annotations
- **Structs**: Custom data structures
- **Type System**: Built-in types and custom type definitions
- **Functional Programming**: Lambda expressions and pipe operator
- **Access Modifiers**: Public, private, protected, and internal visibility
- **Standard Library**: IO module for input/output operations

## Project Structure

```
ContractIR/
├── Contract.Cli/           # Command-line interface
│   ├── Program.cs          # Main entry point
│   ├── TestRunner.cs       # Test execution
│   └── examples/           # Example Contract files
├── Contract.Compiler/      # Compiler implementation
│   ├── AST/                # Abstract Syntax Tree definitions
│   ├── Parsing/            # Lexer and Parser
│   ├── CodeGen/            # Code generation
│   ├── Semantics/          # Semantic analysis
│   ├── Diagnostics/        # Error handling
│   └── StandardLibrary/    # Built-in functions
├── tests/                  # Test files
│   ├── success/            # Valid test cases
│   └── failure/            # Expected failure cases
├── docs/                   # Documentation
└── libs/                   # External libraries
```

## Getting Started

### Prerequisites

- .NET SDK (for building the compiler)
- Git

### Building

```bash
# Clone the repository
git clone https://github.com/yourusername/ContractIR.git
cd ContractIR

# Build the solution
dotnet build
```

### Running

```bash
# Run the CLI
dotnet run --project Contract.Cli

# Compile a Contract file
dotnet run --project Contract.Cli -- compile input.ct
```

### Testing

```bash
# Run all tests
dotnet test

# Run specific test
dotnet run --project Contract.Cli -- test tests/success/HelloWorld.ct
```

## Example Code

```ct
Contract Program {
    static fn Main() {
        IO.Println("Hello, World!");
        
        var x: int = 5;
        var y: int = 10;
        
        if (x < y) {
            IO.Println("x is less than y");
        }
        
        // Lambda functions and pipe operator
        let inc: Int = fun x -> x + 1;
        let result: int = 5 |> inc;
        IO.Println(result);  // 6
    }
}
```

## Documentation

- [Language Reference](docs/CONTRACT_LANGUAGE.md) - Complete language specification
- [Language Specification](docs/LANGUAGE_SPEC_v1.md) - Formal grammar and semantics
- [Implementation Plan](docs/LANGUAGE_PLAN.md) - Roadmap and development plans

## Compiler Architecture

### Lexer (`Contract.Compiler/Parsing/Lexer.cs`)
- Tokenizes source code into tokens
- Handles comments, strings, numbers, and identifiers
- Supports line directives (`#line N`)

### Parser (`Contract.Compiler/Parsing/Parser.cs`)
- Recursive descent parser
- Builds Abstract Syntax Tree (AST)
- Error recovery and diagnostics

### AST (`Contract.Compiler/AST/Ast.cs`)
- Defines all AST node types
- Supports contracts, functions, structs, statements, and expressions

### Code Generation
- `IRCodeGenerator.cs` - Intermediate representation generation
- `BytecodeEmitter.cs` - CIL1 bytecode emission

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

See LICENSE file for details.

## Acknowledgments

- Inspired by modern programming language design
- Built with .NET and C#
