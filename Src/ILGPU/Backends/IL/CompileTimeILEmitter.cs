// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2025 ILGPU Project
//                                    www.ilgpu.net
//
// File: CompileTimeILEmitter.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace ILGPU.Backends.IL
{
    /// <summary>
    /// Represents a compile-time IL emitter that generates source code instead of
    /// runtime IL for native AOT compatibility. This replaces System.Reflection.Emit
    /// usage for AOT scenarios.
    /// </summary>
    public readonly struct CompileTimeILEmitter : IILEmitter
    {
        #region Instance

        private readonly StringBuilder sourceBuilder;
        private readonly List<CompileTimeLocal> locals;
        private readonly List<CompileTimeLabel> labels;
        private readonly int indentLevel;

        /// <summary>
        /// Constructs a new compile-time IL emitter.
        /// </summary>
        /// <param name="sourceBuilder">The string builder for generated source.</param>
        /// <param name="indentLevel">The current indentation level.</param>
        public CompileTimeILEmitter(StringBuilder sourceBuilder, int indentLevel = 0)
        {
            Debug.Assert(sourceBuilder != null, "Invalid source builder");
            this.sourceBuilder = sourceBuilder;
            this.indentLevel = indentLevel;
            
            locals = new List<CompileTimeLocal>();
            labels = new List<CompileTimeLabel>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the underlying source builder.
        /// </summary>
        public StringBuilder SourceBuilder => sourceBuilder;

        #endregion

        #region Methods

        /// <summary>
        /// Emits an indented line of code.
        /// </summary>
        private void EmitLine(string code)
        {
            var indent = new string(' ', indentLevel * 4);
            sourceBuilder.AppendLine($"{indent}{code}");
        }

        /// <summary>
        /// Emits an indented code fragment without a newline.
        /// </summary>
        private void Emit(string code)
        {
            var indent = new string(' ', indentLevel * 4);
            sourceBuilder.Append($"{indent}{code}");
        }

        /// <summary cref="IILEmitter.DeclareLocal(Type)"/>
        public ILLocal DeclareLocal(Type type)
        {
            var localIndex = locals.Count;
            var variableName = $"local_{localIndex}";
            var compileTimeLocal = new CompileTimeLocal(localIndex, type, variableName);
            locals.Add(compileTimeLocal);

            // Generate variable declaration
            EmitLine($"{GetTypeString(type)} {variableName};");

            return new ILLocal(localIndex, type);
        }

        /// <summary cref="IILEmitter.DeclarePinnedLocal(Type)"/>
        public ILLocal DeclarePinnedLocal(Type type)
        {
            var localIndex = locals.Count;
            var variableName = $"pinnedLocal_{localIndex}";
            var compileTimeLocal = new CompileTimeLocal(localIndex, type, variableName) { IsPinned = true };
            locals.Add(compileTimeLocal);

            // Generate pinned variable declaration
            EmitLine($"fixed ({GetTypeString(type)}* {variableName}Ptr = &{variableName})");
            EmitLine("{");
            EmitLine($"    {GetTypeString(type)} {variableName};");

            return new ILLocal(localIndex, type);
        }

        /// <summary cref="IILEmitter.DeclareLabel"/>
        public ILLabel DeclareLabel()
        {
            var labelIndex = labels.Count;
            var labelName = $"Label_{labelIndex}";
            var compileTimeLabel = new CompileTimeLabel(labelIndex, labelName);
            labels.Add(compileTimeLabel);

            return new ILLabel(labelIndex);
        }

        /// <summary cref="IILEmitter.MarkLabel(ILLabel)"/>
        public void MarkLabel(ILLabel label)
        {
            var compileTimeLabel = labels[label.Index];
            EmitLine($"{compileTimeLabel.Name}:");
        }

        /// <summary cref="IILEmitter.Emit(LocalOperation, ILLocal)"/>
        public void Emit(LocalOperation operation, ILLocal local)
        {
            var compileTimeLocal = locals[local.Index];
            
            switch (operation)
            {
                case LocalOperation.Load:
                    sourceBuilder.Append(compileTimeLocal.VariableName);
                    break;
                case LocalOperation.LoadAddress:
                    sourceBuilder.Append($"&{compileTimeLocal.VariableName}");
                    break;
                case LocalOperation.Store:
                    // This is handled by the assignment operation
                    sourceBuilder.Append($"{compileTimeLocal.VariableName} = ");
                    break;
            }
        }

        /// <summary cref="IILEmitter.Emit(ArgumentOperation, int)"/>
        public void Emit(ArgumentOperation operation, int argumentIndex)
        {
            var argName = $"arg{argumentIndex}";
            
            switch (operation)
            {
                case ArgumentOperation.Load:
                    sourceBuilder.Append(argName);
                    break;
                case ArgumentOperation.LoadAddress:
                    sourceBuilder.Append($"&{argName}");
                    break;
            }
        }

        /// <summary cref="IILEmitter.EmitCall(MethodInfo)"/>
        public void EmitCall(MethodInfo target)
        {
            if (target.IsStatic)
            {
                sourceBuilder.Append($"{GetTypeString(target.DeclaringType)}.{target.Name}(");
            }
            else
            {
                // For instance methods, the target should already be on the stack
                sourceBuilder.Append($".{target.Name}(");
            }
            
            // Parameters would be added by the calling code
            sourceBuilder.Append(")");
        }

        /// <summary cref="IILEmitter.EmitNewObject(ConstructorInfo)"/>
        public void EmitNewObject(ConstructorInfo info)
        {
            sourceBuilder.Append($"new {GetTypeString(info.DeclaringType)}(");
            // Parameters would be added by the calling code
            sourceBuilder.Append(")");
        }

        /// <summary cref="IILEmitter.EmitAlloca(int)"/>
        public void EmitAlloca(int size) => sourceBuilder.Append($"stackalloc byte[{size}]");

        /// <summary cref="IILEmitter.EmitConstant(string)"/>
        public void EmitConstant(string constant) => sourceBuilder.Append($"\"{EscapeString(constant)}\"");

        /// <summary cref="IILEmitter.EmitConstant(int)"/>
        public void EmitConstant(int constant) => sourceBuilder.Append(constant.ToString());

        /// <summary cref="IILEmitter.EmitConstant(long)"/>
        public void EmitConstant(long constant) => sourceBuilder.Append($"{constant}L");

        /// <summary cref="IILEmitter.EmitConstant(float)"/>
        public void EmitConstant(float constant) => sourceBuilder.Append($"{constant}f");

        /// <summary cref="IILEmitter.EmitConstant(double)"/>
        public void EmitConstant(double constant) => sourceBuilder.Append($"{constant}d");

        /// <summary cref="IILEmitter.Emit(OpCode)"/>
        public void Emit(OpCode opCode)
        {
            // Convert OpCodes to equivalent C# expressions
            switch (opCode.Name.ToLowerInvariant())
            {
                case "add":
                    sourceBuilder.Append(" + ");
                    break;
                case "sub":
                    sourceBuilder.Append(" - ");
                    break;
                case "mul":
                    sourceBuilder.Append(" * ");
                    break;
                case "div":
                    sourceBuilder.Append(" / ");
                    break;
                case "rem":
                    sourceBuilder.Append(" % ");
                    break;
                case "and":
                    sourceBuilder.Append(" & ");
                    break;
                case "or":
                    sourceBuilder.Append(" | ");
                    break;
                case "xor":
                    sourceBuilder.Append(" ^ ");
                    break;
                case "not":
                    sourceBuilder.Append("~");
                    break;
                case "dup":
                    // Duplication would need special handling in context
                    break;
                case "pop":
                    // Pop would need special handling in context
                    break;
                case "ret":
                    EmitLine("return;");
                    break;
                case "nop":
                    EmitLine("// nop");
                    break;
                default:
                    EmitLine($"// OpCode: {opCode.Name}");
                    break;
            }
        }

        /// <summary cref="IILEmitter.Emit(OpCode, ILLabel)"/>
        public void Emit(OpCode opCode, ILLabel label)
        {
            var compileTimeLabel = labels[label.Index];
            
            switch (opCode.Name.ToLowerInvariant())
            {
                case "br":
                    EmitLine($"goto {compileTimeLabel.Name};");
                    break;
                case "brtrue":
                    EmitLine($"if (/* condition */) goto {compileTimeLabel.Name};");
                    break;
                case "brfalse":
                    EmitLine($"if (!(/* condition */)) goto {compileTimeLabel.Name};");
                    break;
                default:
                    EmitLine($"// Conditional branch {opCode.Name} to {compileTimeLabel.Name}");
                    break;
            }
        }

        /// <summary cref="IILEmitter.Emit(OpCode, Type)"/>
        public void Emit(OpCode opCode, Type type)
        {
            switch (opCode.Name.ToLowerInvariant())
            {
                case "castclass":
                    sourceBuilder.Append($"({GetTypeString(type)})");
                    break;
                case "isinst":
                    sourceBuilder.Append($" is {GetTypeString(type)}");
                    break;
                case "newarr":
                    sourceBuilder.Append($"new {GetTypeString(type)}[");
                    break;
                case "ldelem":
                    sourceBuilder.Append($"[/* index */]");
                    break;
                case "stelem":
                    sourceBuilder.Append($"[/* index */] = ");
                    break;
                default:
                    sourceBuilder.Append($"/* {opCode.Name} {GetTypeString(type)} */");
                    break;
            }
        }

        /// <summary cref="IILEmitter.Emit(OpCode, FieldInfo)"/>
        public void Emit(OpCode opCode, FieldInfo field)
        {
            switch (opCode.Name.ToLowerInvariant())
            {
                case "ldfld":
                    sourceBuilder.Append($".{field.Name}");
                    break;
                case "ldsfld":
                    sourceBuilder.Append($"{GetTypeString(field.DeclaringType)}.{field.Name}");
                    break;
                case "stfld":
                    sourceBuilder.Append($".{field.Name} = ");
                    break;
                case "stsfld":
                    sourceBuilder.Append($"{GetTypeString(field.DeclaringType)}.{field.Name} = ");
                    break;
                default:
                    sourceBuilder.Append($"/* {opCode.Name} {field.Name} */");
                    break;
            }
        }

        /// <summary cref="IILEmitter.EmitSwitch(ILLabel[])"/>
        public void EmitSwitch(ILLabel[] labels)
        {
            EmitLine("switch (/* value */)");
            EmitLine("{");
            
            for (int i = 0; i < labels.Length; i++)
            {
                var compileTimeLabel = this.labels[labels[i].Index];
                EmitLine($"    case {i}: goto {compileTimeLabel.Name};");
            }
            
            EmitLine("}");
        }

        /// <summary cref="IILEmitter.EmitWriteLine"/>
        public void EmitWriteLine(string message) => EmitLine($"Console.WriteLine(\"{EscapeString(message)}\");");

        /// <summary cref="IILEmitter.Finish"/>
        public void Finish()
        {
            // Close any open pinned blocks
            foreach (var local in locals)
            {
                if (local.IsPinned)
                {
                    EmitLine("}");
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the C# string representation of a type.
        /// </summary>
        private static string GetTypeString(Type type)
        {
            if (type == null) return "object";
            
            return type switch
            {
                _ when type == typeof(int) => "int",
                _ when type == typeof(long) => "long",
                _ when type == typeof(float) => "float",
                _ when type == typeof(double) => "double",
                _ when type == typeof(bool) => "bool",
                _ when type == typeof(byte) => "byte",
                _ when type == typeof(sbyte) => "sbyte",
                _ when type == typeof(short) => "short",
                _ when type == typeof(ushort) => "ushort",
                _ when type == typeof(uint) => "uint",
                _ when type == typeof(ulong) => "ulong",
                _ when type == typeof(char) => "char",
                _ when type == typeof(string) => "string",
                _ when type == typeof(object) => "object",
                _ when type == typeof(void) => "void",
                _ => type.Name
            };
        }

        /// <summary>
        /// Escapes a string for use in C# source code.
        /// </summary>
        private static string EscapeString(string input) => input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a compile-time local variable.
        /// </summary>
        private record CompileTimeLocal(
            int Index,
            Type Type,
            string VariableName)
        {
            public bool IsPinned { get; init; } = false;
        }

        /// <summary>
        /// Represents a compile-time label.
        /// </summary>
        private record CompileTimeLabel(
            int Index,
            string Name);

        #endregion
    }
}