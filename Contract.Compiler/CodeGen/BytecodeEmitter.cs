// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Text;
// using ObjectIR.Core.AST;
//
// namespace Contract.Compiler.CodeGen
// {
//     public enum Opcode : byte
//     {
//         // Core instructions (0-17)
//         NOP = 0,
//         LDC_I4 = 1,
//         LDC_STR = 2,
//         PRINT_CONST = 3,
//         PRINT_INT = 4,
//         LOAD_LOCAL = 5,
//         STORE_LOCAL = 6,
//         ADD = 7,
//         SUB = 8,
//         MUL = 9,
//         DIV = 10,
//         LT = 11,
//         LE = 12,
//         EQ = 13,
//         JZ = 14,
//         JMP = 15,
//         CALL = 16,
//         RET = 17,
//
//         // Additional data types (18-22)
//         LDC_F4 = 18,      // Load float constant
//         LDC_BOOL = 19,    // Load boolean constant
//         LDC_CHAR = 20,    // Load char constant
//         LDC_I8 = 21,      // Load 64-bit int constant
//         LDC_NULL = 22,    // Load null reference
//
//         // Additional comparisons (23-28)
//         GT = 23,          // Greater than
//         GE = 24,          // Greater than or equal
//         NE = 25,          // Not equal
//         CMP = 26,         // General compare (-1, 0, 1 result)
//
//         // Logical operators (27-29)
//         AND = 27,         // Logical AND
//         OR = 28,          // Logical OR
//         NOT = 29,         // Logical NOT
//
//         // Bitwise operators (30-35)
//         BIT_AND = 30,     // Bitwise AND
//         BIT_OR = 31,      // Bitwise OR
//         BIT_XOR = 32,     // Bitwise XOR
//         BIT_NOT = 33,     // Bitwise NOT
//         SHL = 34,         // Shift left
//         SHR = 35,         // Shift right
//
//         // Stack manipulation (36-38)
//         DUP = 36,         // Duplicate top of stack
//         POP = 37,         // Pop top of stack
//         SWAP = 38,        // Swap top two stack elements
//
//         // Array operations (39-42)
//         NEWARR = 39,      // Create new array
//         LDELEM = 40,      // Load array element
//         STELEM = 41,      // Store array element
//         LDLEN = 42,       // Load array length
//
//         // Object operations (43-46)
//         NEWOBJ = 43,      // Create new object
//         LDFLD = 44,       // Load field
//         STFLD = 45,       // Store field
//         CALLVIRT = 46,    // Virtual method call
//
//         // Type conversions (47-52)
//         CONV_I4_F4 = 47,  // int to float
//         CONV_F4_I4 = 48,  // float to int
//         CONV_I4_I8 = 49,  // int to long
//         CONV_I8_I4 = 50,  // long to int
//         CONV_I4_BOOL = 51,// int to bool
//         CONV_BOOL_I4 = 52,// bool to int
//
//         // Additional jumps (53-57)
//         JNZ = 53,         // Jump if not zero
//         JG = 54,          // Jump if greater
//         JGE = 55,         // Jump if greater or equal
//         JL = 56,          // Jump if less
//         JLE = 57,         // Jump if less or equal
//
//         // Advanced math (58-61)
//         REM = 58,         // Remainder/modulo
//         NEG = 59,         // Negation
//         INC = 60,         // Increment
//         DEC = 61,         // Decrement
//
//         // String operations (62-64)
//         CONCAT = 62,      // String concatenation
//         STRLEN = 63,      // String length
//         SUBSTR = 64,      // Substring
//
//         // Exception handling (65-67)
//         THROW = 65,       // Throw exception
//         TRY = 66,         // Start try block
//         CATCH = 67,       // Catch exception
//     }
//
//     public class MethodInfo
//     {
//         public string Name { get; }
//         public int ArgCount { get; }
//         public int LocalCount { get; }
//         public int CodeOffset { get; set; }
//         public int CodeLength { get; set; }
//
//         public MethodInfo(string name, int argCount, int localCount)
//         {
//             Name = name;
//             ArgCount = argCount;
//             LocalCount = localCount;
//         }
//     }
//
//     public class BytecodeEmitter
//     {
//         // private readonly List<byte> _code = new();
//         // private readonly List<string> _constants = new();
//         // private readonly List<MethodInfo> _methods = new();
//         // private readonly Dictionary<string, int> _constantMap = new();
//         public static ModuleNode Module;
//
//         public ModuleNode Code => Module;
//
//         // public int CurrentCodeOffset => _code.Count;
//
//         // public int AddConstant(string value)
//         // {
//         //     if (_constantMap.TryGetValue(value, out int index))
//         //     {
//         //         return index;
//         //     }
//         //     index = _constants.Count;
//         //     _constants.Add(value);
//         //     _constantMap[value] = index;
//         //     return index;
//         // }
//
//         // public int AddMethod(string name, int argCount, int localCount)
//         // {
//         //     var method = new MethodInfo(name, argCount, localCount);
//         //     _methods.Add(method);
//         //     return _methods.Count - 1;
//         // }
//         //
//         // public void Emit(Opcode opcode)
//         // {
//         //     _code.Add((byte)opcode);
//         // }
//
//         public void EmitInt32(int value)
//         {
//             _code.AddRange(BitConverter.GetBytes(value));
//         }
//
//         public void EmitUInt32(uint value)
//         {
//             _code.AddRange(BitConverter.GetBytes(value));
//         }
//
//         public void EmitByte(byte value)
//         {
//             _code.Add(value);
//         }
//
//         public void EmitLoadConst(int value)
//         {
//             Emit(Opcode.LDC_I4);
//             EmitInt32(value);
//         }
//
//         public void EmitLoadString(string value)
//         {
//             int constIndex = AddConstant(value);
//             Emit(Opcode.LDC_STR);
//             EmitUInt32((uint)constIndex);
//         }
//
//         public void EmitLoadLocal(int index)
//         {
//             Emit(Opcode.LOAD_LOCAL);
//             EmitByte((byte)index);
//         }
//
//         public void EmitStoreLocal(int index)
//         {
//             Emit(Opcode.STORE_LOCAL);
//             EmitByte((byte)index);
//         }
//
//         public void EmitBinaryOp(Opcode op)
//         {
//             Emit(op);
//         }
//
//         public void EmitJump(Opcode jumpOp, int targetOffset)
//         {
//             Emit(jumpOp);
//             EmitUInt32((uint)targetOffset);
//         }
//
//         public void EmitCall(int methodIndex)
//         {
//             Emit(Opcode.CALL);
//             EmitUInt32((uint)methodIndex);
//         }
//
//         public void EmitReturn()
//         {
//             Emit(Opcode.RET);
//         }
//
//         public void EmitPrintConst()
//         {
//             Emit(Opcode.PRINT_CONST);
//         }
//
//         public void EmitPrintInt()
//         {
//             Emit(Opcode.PRINT_INT);
//         }
//
//         // New emit methods for expanded instruction set
//         public void EmitLoadFloat(float value)
//         {
//             Emit(Opcode.LDC_F4);
//             EmitInt32(BitConverter.SingleToInt32Bits(value));
//         }
//
//         public void EmitLoadBool(bool value)
//         {
//             Emit(Opcode.LDC_BOOL);
//             EmitByte((byte)(value ? 1 : 0));
//         }
//
//         public void EmitLoadChar(char value)
//         {
//             Emit(Opcode.LDC_CHAR);
//             EmitUInt32((uint)value);
//         }
//
//         public void EmitLoadLong(long value)
//         {
//             Emit(Opcode.LDC_I8);
//             EmitInt32((int)(value & 0xFFFFFFFF));
//             EmitInt32((int)(value >> 32));
//         }
//
//         public void EmitLoadNull()
//         {
//             Emit(Opcode.LDC_NULL);
//         }
//
//         public void EmitCompare(Opcode compareOp)
//         {
//             Emit(compareOp);
//         }
//
//         public void EmitLogicalOp(Opcode logicalOp)
//         {
//             Emit(logicalOp);
//         }
//
//         public void EmitBitwiseOp(Opcode bitwiseOp)
//         {
//             Emit(bitwiseOp);
//         }
//
//         public void EmitStackOp(Opcode stackOp)
//         {
//             Emit(stackOp);
//         }
//
//         public void EmitDup()
//         {
//             Emit(Opcode.DUP);
//         }
//
//         public void EmitPop()
//         {
//             Emit(Opcode.POP);
//         }
//
//         public void EmitSwap()
//         {
//             Emit(Opcode.SWAP);
//         }
//
//         public void EmitNewArray()
//         {
//             Emit(Opcode.NEWARR);
//         }
//
//         public void EmitLoadElement()
//         {
//             Emit(Opcode.LDELEM);
//         }
//
//         public void EmitStoreElement()
//         {
//             Emit(Opcode.STELEM);
//         }
//
//         public void EmitLoadLength()
//         {
//             Emit(Opcode.LDLEN);
//         }
//
//         public void EmitNewObject()
//         {
//             Emit(Opcode.NEWOBJ);
//         }
//
//         public void EmitLoadField(int fieldIndex)
//         {
//             Emit(Opcode.LDFLD);
//             EmitByte((byte)fieldIndex);
//         }
//
//         public void EmitStoreField(int fieldIndex)
//         {
//             Emit(Opcode.STFLD);
//             EmitByte((byte)fieldIndex);
//         }
//
//         public void EmitConvert(Opcode convertOp)
//         {
//             Emit(convertOp);
//         }
//
//         public void EmitMathOp(Opcode mathOp)
//         {
//             Emit(mathOp);
//         }
//
//         public void EmitRemainder()
//         {
//             Emit(Opcode.REM);
//         }
//
//         public void EmitNegate()
//         {
//             Emit(Opcode.NEG);
//         }
//
//         public void EmitIncrement()
//         {
//             Emit(Opcode.INC);
//         }
//
//         public void EmitDecrement()
//         {
//             Emit(Opcode.DEC);
//         }
//
//         public void EmitConcat()
//         {
//             Emit(Opcode.CONCAT);
//         }
//
//         public void EmitStringLength()
//         {
//             Emit(Opcode.STRLEN);
//         }
//
//         public void SetMethodCode(int methodIndex, int codeOffset, int codeLength)
//         {
//             _methods[methodIndex].CodeOffset = codeOffset;
//             _methods[methodIndex].CodeLength = codeLength;
//         }
//
//         public byte[] GenerateCIL1()
//         {
//             using var ms = new MemoryStream();
//             using var writer = new BinaryWriter(ms, Encoding.UTF8);
//
//             // Header: "CIL1"
//             writer.Write(Encoding.ASCII.GetBytes("CIL1"));
//
//             // Code length and code
//             writer.Write((uint)_code.Count);
//             writer.Write(_code.ToArray());
//
//             // Constants length and constants
//             int constsLen = _constants.Sum(c => Encoding.UTF8.GetByteCount(c) + 1); // +1 for null terminator
//             writer.Write((uint)constsLen);
//             foreach (var constant in _constants)
//             {
//                 writer.Write(Encoding.UTF8.GetBytes(constant));
//                 writer.Write((byte)0); // null terminator
//             }
//
//             // Method count
//             writer.Write((uint)_methods.Count);
//
//             // Method table
//             foreach (var method in _methods)
//             {
//                 int nameOffset = GetConstantOffset(method.Name);
//                 writer.Write((uint)nameOffset);
//                 writer.Write((uint)method.ArgCount);
//                 writer.Write((uint)method.LocalCount);
//                 writer.Write((uint)method.CodeOffset);
//                 writer.Write((uint)method.CodeLength);
//             }
//
//             return ms.ToArray();
//         }
//
//         private int GetConstantOffset(string constant)
//         {
//             int offset = 0;
//             for (int i = 0; i < _constants.Count; i++)
//             {
//                 if (_constants[i] == constant)
//                 {
//                     return offset;
//                 }
//                 offset += Encoding.UTF8.GetByteCount(_constants[i]) + 1; // +1 for null terminator
//             }
//             throw new InvalidOperationException($"Constant '{constant}' not found");
//         }
//
//         public void FixJumpTarget(int jumpInstructionOffset, int targetOffset)
//         {
//             // jumpInstructionOffset points to the opcode (JZ or JMP)
//             // The offset immediate starts at jumpInstructionOffset + 1
//             uint offset = (uint)targetOffset;
//             _code[jumpInstructionOffset + 1] = (byte)(offset & 0xFF);
//             _code[jumpInstructionOffset + 2] = (byte)((offset >> 8) & 0xFF);
//             _code[jumpInstructionOffset + 3] = (byte)((offset >> 16) & 0xFF);
//             _code[jumpInstructionOffset + 4] = (byte)((offset >> 24) & 0xFF);
//         }
//
//         public void WriteToFile(string path)
//         {
//             File.WriteAllBytes(path, GenerateCIL1());
//         }
//         // current code offset
//         public int CodeOffset => _code.Count;
//     }
// }