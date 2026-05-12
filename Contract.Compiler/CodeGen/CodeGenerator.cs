// using System;
// using System.Collections.Generic;
// using Contract.Compiler.AST;
// using Contract.Compiler.Diagnostics;
// using ObjectIR.Core.Builder;
//
// namespace Contract.Compiler.CodeGen
// {
//     public class CodeGenerator
//     {
//         private readonly IRBuilder _emitter;
//         private readonly DiagnosticBag _diagnostics;
//         private readonly Dictionary<string, int> _methodIndices = new();
//         private readonly Dictionary<string, int> _localIndices = new();
//         private int _localCount;
//         private int _currentMethodIndex = -1;
//
//         public CodeGenerator(DiagnosticBag diagnostics)
//         {
//             _emitter = new IRBuilder("Temp");
//             _diagnostics = diagnostics;
//         }
//
//         public void Generate(Program program)
//         {
//             // First pass: collect all methods
//             CollectMethods(program);
//
//             // Second pass: generate code for each method
//             GenerateMethods(program);
//         }
//
//         private void CollectMethods(Program program)
//         {
//             foreach (var contract in program.Contracts)
//             {
//                 foreach (var member in contract.Members)
//                 {
//                     if (member is FunctionDeclaration func)
//                     {
//                         string fullName = $"{contract.Name}.{func.Name}";
//                         _emitter.AddConstant(fullName); // Add method name as constant
//                         int methodIndex = _emitter.AddMethod(fullName, func.Parameters.Count, 16); // Default local count
//                         _methodIndices[fullName] = methodIndex;
//                     }
//                 }
//             }
//
//             foreach (var func in program.Functions)
//             {
//                 string fullName = func.Name!;
//                 _emitter.AddConstant(fullName); // Add method name as constant
//                 int methodIndex = _emitter.AddMethod(fullName, func.Parameters.Count, 16);
//                 _methodIndices[fullName] = methodIndex;
//             }
//         }
//
//         private void GenerateMethods(Program program)
//         {
//             foreach (var contract in program.Contracts)
//             {
//                 foreach (var member in contract.Members)
//                 {
//                     if (member is FunctionDeclaration func)
//                     {
//                         GenerateFunction(func, contract.Name);
//                     }
//                 }
//             }
//
//             foreach (var func in program.Functions)
//             {
//                 GenerateFunction(func, null);
//             }
//         }
//
//         private void GenerateFunction(FunctionDeclaration func, string? contractName)
//         {
//             string fullName = contractName != null ? $"{contractName}.{func.Name}" : func.Name!;
//             _currentMethodIndex = _methodIndices[fullName];
//
//             // Clear local state
//             _localIndices.Clear();
//             _localCount = 0;
//
//             // Add parameters as locals
//             foreach (var param in func.Parameters)
//             {
//                 _localIndices[param.Name] = _localCount++;
//             }
//
//             int startOffset = _emitter.CurrentCodeOffset;
//
//             if (func.Body != null)
//             {
//                 GenerateStatement(func.Body);
//             }
//
//             // Add implicit return if not present
//             // Check if the last statement in the body is a return
//             bool endsWithReturn = false;
//             if (func.Body is BlockStatement block && block.Statements.Count > 0)
//             {
//                 endsWithReturn = block.Statements[^1] is ReturnStatement;
//             }
//
//             if (!endsWithReturn)
//             {
//                 _emitter.EmitReturn();
//             }
//
//             int endOffset = _emitter.CurrentCodeOffset;
//             _emitter.SetMethodCode(_currentMethodIndex, startOffset, endOffset - startOffset);
//         }
//
//         private void GenerateStatement(Statement statement)
//         {
//             switch (statement)
//             {
//                 case BlockStatement block:
//                     foreach (var stmt in block.Statements)
//                     {
//                         GenerateStatement(stmt);
//                     }
//                     break;
//
//                 case VariableDeclaration varDecl:
//                     if (varDecl.Initializer != null)
//                     {
//                         GenerateExpression(varDecl.Initializer);
//                         int localIndex = _localIndices[varDecl.Name];
//                         _emitter.EmitStoreLocal(localIndex);
//                     }
//                     break;
//
//                 case ExpressionStatement exprStmt:
//                     GenerateExpression(exprStmt.Expression);
//                     // Expressions that leave values on stack should be consumed
//                     // For now, assume all expressions are consumed
//                     break;
//
//                 case IfStatement ifStmt:
//                     GenerateIfStatement(ifStmt);
//                     break;
//
//                 case WhileStatement whileStmt:
//                     GenerateWhileStatement(whileStmt);
//                     break;
//
//                 case SwitchStatement switchStmt:
//                     GenerateSwitchStatement(switchStmt);
//                     break;
//
//                 case ReturnStatement retStmt:
//                     if (retStmt.Value != null)
//                     {
//                         GenerateExpression(retStmt.Value);
//                     }
//                     _emitter.EmitReturn();
//                     break;
//
//                 default:
//                     _diagnostics.AddError($"Unsupported statement type: {statement.GetType().Name}",
//                         statement.Line, statement.Column);
//                     break;
//             }
//         }
//
//         private void GenerateIfStatement(IfStatement ifStmt)
//         {
//             GenerateExpression(ifStmt.Condition);
//             int jumpOverThenOffset = _emitter.CurrentCodeOffset;
//             _emitter.EmitJump(Opcode.JZ, 0); // Placeholder
//
//             GenerateStatement(ifStmt.ThenBranch);
//
//             if (ifStmt.ElseBranch != null)
//             {
//                 int jumpOverElseOffset = _emitter.CurrentCodeOffset;
//                 _emitter.EmitJump(Opcode.JMP, 0); // Placeholder
//
//                 // Fix the jump over then
//                 int afterThenOffset = _emitter.CurrentCodeOffset;
//                 _emitter.FixJumpTarget(jumpOverThenOffset, afterThenOffset);
//
//                 GenerateStatement(ifStmt.ElseBranch);
//
//                 // Fix the jump over else
//                 int afterElseOffset = _emitter.CurrentCodeOffset;
//                 _emitter.FixJumpTarget(jumpOverElseOffset, afterElseOffset);
//             }
//             else
//             {
//                 // Fix the jump over then
//                 int afterThenOffset = _emitter.CurrentCodeOffset;
//                 _emitter.FixJumpTarget(jumpOverThenOffset, afterThenOffset);
//             }
//         }
//
//         private void GenerateWhileStatement(WhileStatement whileStmt)
//         {
//             int loopStart = _emitter.CurrentCodeOffset;
//             GenerateExpression(whileStmt.Condition);
//
//             int jumpOverBodyOffset = _emitter.CurrentCodeOffset;
//             _emitter.EmitJump(Opcode.JZ, 0); // Placeholder
//
//             GenerateStatement(whileStmt.Body);
//
//             // Jump back to start
//             _emitter.EmitJump(Opcode.JMP, loopStart);
//
//             // Fix the jump over body
//             int afterBodyOffset = _emitter.CurrentCodeOffset;
//             _emitter.FixJumpTarget(jumpOverBodyOffset, afterBodyOffset);
//         }
//
//         private void GenerateSwitchStatement(SwitchStatement switchStmt)
//         {
//             // For now, implement as a series of if-else statements
//             // This is a simplified implementation
//             GenerateExpression(switchStmt.Expression);
//
//             var endJumps = new List<int>();
//
//             foreach (var caseStmt in switchStmt.Cases)
//             {
//                 if (caseStmt.Value != null)
//                 {
//                     // Duplicate the switch value
//                     _emitter.Emit(Opcode.LOAD_LOCAL); // This is simplified - need proper dup
//                     _emitter.EmitByte(0); // Assume switch value is in local 0
//                     _emitter.EmitLoadConst(caseStmt.Value.Value);
//                     _emitter.EmitBinaryOp(Opcode.EQ);
//
//                     int jumpOverCaseOffset = _emitter.CurrentCodeOffset;
//                     _emitter.EmitJump(Opcode.JZ, 0); // Placeholder
//
//                     foreach (var stmt in caseStmt.Statements)
//                     {
//                         GenerateStatement(stmt);
//                     }
//
//                     int jumpToEndOffset = _emitter.CurrentCodeOffset;
//                     _emitter.EmitJump(Opcode.JMP, 0); // Placeholder
//                     endJumps.Add(jumpToEndOffset);
//
//                     // Fix jump over case
//                     int afterCaseOffset = _emitter.CurrentCodeOffset;
//                     _emitter.FixJumpTarget(jumpOverCaseOffset, afterCaseOffset);
//                 }
//                 else
//                 {
//                     // Default case
//                     foreach (var stmt in caseStmt.Statements)
//                     {
//                         GenerateStatement(stmt);
//                     }
//                 }
//             }
//
//             // Fix all end jumps
//             int endOffset = _emitter.CurrentCodeOffset;
//             foreach (int jumpOffset in endJumps)
//             {
//                 _emitter.FixJumpTarget(jumpOffset, endOffset);
//             }
//         }
//
//         private void GenerateExpression(Expression expression)
//         {
//             switch (expression)
//             {
//                 case LiteralExpression literal:
//                     if (literal.Value is int intValue)
//                     {
//                         _emitter.EmitLoadConst(intValue);
//                     }
//                     else if (literal.Value is string stringValue)
//                     {
//                         _emitter.EmitLoadString(stringValue);
//                     }
//                     break;
//
//                 case IdentifierExpression identifierExpr:
//                     if (_localIndices.TryGetValue(identifierExpr.Name, out int localIndex))
//                     {
//                         _emitter.EmitLoadLocal(localIndex);
//                     }
//                     else
//                     {
//                         _diagnostics.AddError($"Undefined variable: {identifierExpr.Name}", identifierExpr.Line, identifierExpr.Column);
//                     }
//                     break;
//
//                 case BinaryExpression binary:
//                     GenerateExpression(binary.Left);
//                     GenerateExpression(binary.Right);
//
//                     Opcode op;
//                     switch (binary.Operator)
//                     {
//                         case "+": op = Opcode.ADD; break;
//                         case "-": op = Opcode.SUB; break;
//                         case "*": op = Opcode.MUL; break;
//                         case "/": op = Opcode.DIV; break;
//                         case "<": op = Opcode.LT; break;
//                         case "<=": op = Opcode.LE; break;
//                         case "==": op = Opcode.EQ; break;
//                         default:
//                             _diagnostics.AddError($"Unsupported binary operator: {binary.Operator}",
//                                 binary.Line, binary.Column);
//                             return;
//                     }
//                     _emitter.EmitBinaryOp(op);
//                     break;
//
//                 case CallExpression call:
//                     // Generate arguments
//                     foreach (var arg in call.Arguments)
//                     {
//                         GenerateExpression(arg);
//                     }
//
//                     // Handle built-in functions
//                     if (call.Callee is IdentifierExpression ident)
//                     {
//                         if (ident.Name == "print" || ident.Name == "println")
//                         {
//                             if (call.Arguments.Count == 1)
//                             {
//                                 var arg = call.Arguments[0];
//                                 if (arg is LiteralExpression lit && lit.Value is string)
//                                 {
//                                     _emitter.EmitPrintConst();
//                                 }
//                                 else
//                                 {
//                                     _emitter.EmitPrintInt();
//                                 }
//                             }
//                         }
//                         else
//                         {
//                             // Try to find method
//                             string methodName = GetMethodName(call.Callee);
//                             if (_methodIndices.TryGetValue(methodName, out int methodIndex))
//                             {
//                                 _emitter.EmitCall(methodIndex);
//                             }
//                             else
//                             {
//                                 _diagnostics.AddError($"Undefined function: {methodName}",
//                                     call.Line, call.Column);
//                             }
//                         }
//                     }
//                     else
//                     {
//                         // Try to find method
//                         string methodName = GetMethodName(call.Callee);
//                         if (_methodIndices.TryGetValue(methodName, out int methodIndex))
//                         {
//                             _emitter.EmitCall(methodIndex);
//                         }
//                         else
//                         {
//                             _diagnostics.AddError($"Undefined function: {methodName}",
//                                 call.Line, call.Column);
//                         }
//                     }
//                     break;
//
//                 default:
//                     _diagnostics.AddError($"Unsupported expression type: {expression.GetType().Name}",
//                         expression.Line, expression.Column);
//                     break;
//             }
//         }
//
//         private string GetMethodName(Expression callee)
//         {
//             if (callee is IdentifierExpression identifierExpr)
//             {
//                 return identifierExpr.Name;
//             }
//             // Handle qualified names later
//             return "<unknown>";
//         }
//
//         public void WriteToFile(string path)
//         {
//             _emitter.WriteToFile(path);
//         }
//     }
// }