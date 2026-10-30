//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace nanoFramework.Tools.MetadataProcessor.Tests.Core
{
    [TestClass]
    public class FunctionPointersTests
    {
        private static AssemblyDefinition s_mscorlib;

        private static AssemblyDefinition Mscorlib
        {
            get
            {
                if (s_mscorlib == null)
                {
                    s_mscorlib = AssemblyDefinition.ReadAssembly(TestObjectHelper.MscorlibFullPath);
                }

                return s_mscorlib;
            }
        }

        private static nanoTablesContext CreateContext()
        {
            return new nanoTablesContext(
                Mscorlib,
                null,
                null,
                false,
                false,
                true);
        }

        private static FunctionPointerType CreateFunctionPointer(MethodCallingConvention callingConvention)
        {
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            // delegate*<int, int, int>
            var functionPointer = new FunctionPointerType
            {
                ReturnType = typeSystem.Int32,
                CallingConvention = callingConvention
            };
            functionPointer.Parameters.Add(new ParameterDefinition(typeSystem.Int32));
            functionPointer.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            return functionPointer;
        }

        private static CallSite CreateCallSite(MethodCallingConvention callingConvention)
        {
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            var callSite = new CallSite(typeSystem.Int32)
            {
                CallingConvention = callingConvention
            };
            callSite.Parameters.Add(new ParameterDefinition(typeSystem.Int32));
            callSite.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            return callSite;
        }

        private static byte[] WriteDataType(TypeReference type)
        {
            var table = new nanoSignaturesTable(CreateContext());

            using (var memoryStream = new MemoryStream())
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                nanoBinaryWriter writer = nanoBinaryWriter.CreateBigEndianBinaryWriter(binaryWriter);

                table.WriteDataType(type, writer, false, false, false);

                binaryWriter.Flush();
                return memoryStream.ToArray();
            }
        }

        [TestMethod]
        public void WriteDataType_ManagedFunctionPointer_IsEncodedAsI4()
        {
            byte[] actual = WriteDataType(CreateFunctionPointer(MethodCallingConvention.Default));

            CollectionAssert.AreEqual(new byte[] { (byte)NanoCLRDataType.DATATYPE_I4 }, actual);
        }

        [TestMethod]
        public void WriteDataType_ManagedFunctionPointer_IsEncodedAsInt32()
        {
            CollectionAssert.AreEqual(
                WriteDataType(Mscorlib.MainModule.TypeSystem.Int32),
                WriteDataType(CreateFunctionPointer(MethodCallingConvention.Default)));
        }

        [DataRow(MethodCallingConvention.C)]
        [DataRow(MethodCallingConvention.StdCall)]
        [DataRow(MethodCallingConvention.ThisCall)]
        [DataRow(MethodCallingConvention.FastCall)]
        [DataRow(MethodCallingConvention.Unmanaged)]
        [TestMethod]
        public void WriteDataType_UnmanagedFunctionPointer_Throws(MethodCallingConvention callingConvention)
        {
            Assert.ThrowsException<NotSupportedException>(
                () => WriteDataType(CreateFunctionPointer(callingConvention)));
        }

        [TestMethod]
        public void GetOrCreateSignatureId_CallSite_MatchesEquivalentMethodSignature()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            // static int Foo(int, int)
            var method = new MethodReference("Foo", typeSystem.Int32, typeSystem.Object);
            method.Parameters.Add(new ParameterDefinition(typeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            ushort methodSigId = table.GetOrCreateSignatureId(method);
            ushort callSiteSigId = table.GetOrCreateSignatureId(CreateCallSite(MethodCallingConvention.Default));

            Assert.AreEqual(methodSigId, callSiteSigId);
        }

        [TestMethod]
        public void GetOrCreateSignatureId_CallSite_HasThisIsPartOfTheSignature()
        {
            var table = new nanoSignaturesTable(CreateContext());

            CallSite staticCallSite = CreateCallSite(MethodCallingConvention.Default);
            CallSite instanceCallSite = CreateCallSite(MethodCallingConvention.Default);
            instanceCallSite.HasThis = true;

            Assert.AreNotEqual(
                table.GetOrCreateSignatureId(staticCallSite),
                table.GetOrCreateSignatureId(instanceCallSite));
        }

        [TestMethod]
        public void GetOrCreateSignatureId_CallSite_CustomModifiersAreIgnored()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            // modifier types emitted by C# for 'out', 'in' and 'ref readonly' don't need to exist in the class libraries
            var outAttribute = new TypeReference("System.Runtime.InteropServices", "OutAttribute", Mscorlib.MainModule, Mscorlib.MainModule);
            var inAttribute = new TypeReference("System.Runtime.InteropServices", "InAttribute", Mscorlib.MainModule, Mscorlib.MainModule);

            // static ref int Foo(ref int, ref int, int)
            var method = new MethodReference("Foo", new ByReferenceType(typeSystem.Int32), typeSystem.Object);
            method.Parameters.Add(new ParameterDefinition(new ByReferenceType(typeSystem.Int32)));
            method.Parameters.Add(new ParameterDefinition(new ByReferenceType(typeSystem.Int32)));
            method.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            // delegate*<out int, in int, int, ref readonly int>
            var callSite = new CallSite(new RequiredModifierType(inAttribute, new ByReferenceType(typeSystem.Int32)));
            callSite.Parameters.Add(new ParameterDefinition(new RequiredModifierType(outAttribute, new ByReferenceType(typeSystem.Int32))));
            callSite.Parameters.Add(new ParameterDefinition(new ByReferenceType(new RequiredModifierType(inAttribute, typeSystem.Int32))));
            callSite.Parameters.Add(new ParameterDefinition(new OptionalModifierType(outAttribute, typeSystem.Int32)));

            ushort methodSigId = table.GetOrCreateSignatureId(method);
            ushort callSiteSigId = table.GetOrCreateSignatureId(callSite);

            Assert.AreEqual(methodSigId, callSiteSigId);
        }

        [DataRow(MethodCallingConvention.C)]
        [DataRow(MethodCallingConvention.StdCall)]
        [DataRow(MethodCallingConvention.Unmanaged)]
        [TestMethod]
        public void GetOrCreateSignatureId_UnmanagedCallSite_Throws(MethodCallingConvention callingConvention)
        {
            var table = new nanoSignaturesTable(CreateContext());

            Assert.ThrowsException<NotSupportedException>(
                () => table.GetOrCreateSignatureId(CreateCallSite(callingConvention)));
        }

        [TestMethod]
        public void CalculateStackSize_Calli()
        {
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            var target = new MethodReference("Add", typeSystem.Int32, typeSystem.Object);
            target.Parameters.Add(new ParameterDefinition(typeSystem.Int32));
            target.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            var method = new MethodDefinition("Test", MethodAttributes.Static, typeSystem.Int32);
            ILProcessor il = method.Body.GetILProcessor();

            // return ((delegate*<int, int, int>)&Add)(1, 2) + 3;
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldftn, target);
            il.Emit(OpCodes.Calli, CreateCallSite(MethodCallingConvention.Default));
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);

            // 2 args + function pointer
            Assert.AreEqual((byte)3, CodeWriter.CalculateStackSize(method.Body));
        }

        [TestMethod]
        public void CalculateStackSize_CalliVoidInstance()
        {
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            var target = new MethodReference("Run", typeSystem.Void, typeSystem.Object) { HasThis = true };
            target.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            var callSite = new CallSite(typeSystem.Void) { HasThis = true };
            callSite.Parameters.Add(new ParameterDefinition(typeSystem.Int32));

            var method = new MethodDefinition("Test", MethodAttributes.Static, typeSystem.Int32);
            ILProcessor il = method.Body.GetILProcessor();

            // instance call through a function pointer, then push a constant
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldftn, target);
            il.Emit(OpCodes.Calli, callSite);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            // calli must pop this + 1 arg + function pointer, so the 4 constants pushed after it start from an empty stack
            Assert.AreEqual((byte)4, CodeWriter.CalculateStackSize(method.Body));
        }

        private static FunctionPointerType CreateFunctionPointer(TypeReference returnType, params TypeReference[] parameterTypes)
        {
            var functionPointer = new FunctionPointerType { ReturnType = returnType };

            foreach (TypeReference parameterType in parameterTypes)
            {
                functionPointer.Parameters.Add(new ParameterDefinition(parameterType));
            }

            return functionPointer;
        }

        private static byte[] ReadSignature(nanoSignaturesTable table, ushort sigId, int length)
        {
            using (var memoryStream = new MemoryStream())
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                table.Write(nanoBinaryWriter.CreateLittleEndianBinaryWriter(binaryWriter));

                binaryWriter.Flush();

                byte[] signature = new byte[length];
                Array.Copy(memoryStream.ToArray(), sigId, signature, 0, length);

                return signature;
            }
        }

        private static MethodReference CreateMethod(TypeReference returnType, params TypeReference[] parameterTypes)
        {
            var method = new MethodReference("Foo", returnType, Mscorlib.MainModule.TypeSystem.Object);

            foreach (TypeReference parameterType in parameterTypes)
            {
                method.Parameters.Add(new ParameterDefinition(parameterType));
            }

            return method;
        }

        [TestMethod]
        public void MethodSignature_FunctionPointerParameter_KeepsFunctionPointerSignature()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            // static void Foo(delegate*<int, int, int>)
            ushort sigId = table.GetOrCreateSignatureId(CreateMethod(typeSystem.Void, CreateFunctionPointer(MethodCallingConvention.Default)));
            byte[] signature = ReadSignature(table, sigId, 7);
            ushort functionPointerSigId = table.GetOrCreateFunctionPointerSignatureId(CreateFunctionPointer(MethodCallingConvention.Default));

            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x00,
                    0x01,
                    (byte)NanoCLRDataType.DATATYPE_VOID,
                    (byte)NanoCLRDataType.DATATYPE_TYPE_FNPTR,
                    (byte)(functionPointerSigId & 0xFF),
                    (byte)(functionPointerSigId >> 8),
                    (byte)NanoCLRDataType.DATATYPE_I4
                },
                signature);

            // the function pointer signature is the signature of the target methods: static int Foo(int, int)
            Assert.AreEqual(
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, typeSystem.Int32, typeSystem.Int32)),
                functionPointerSigId);
        }

        [TestMethod]
        public void MethodSignatures_DifferingOnlyByFunctionPointerTypes_AreDistinct()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            var ids = new List<ushort>
            {
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, CreateFunctionPointer(typeSystem.Int32))),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, CreateFunctionPointer(typeSystem.Int64))),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, CreateFunctionPointer(typeSystem.Int32, typeSystem.Int32))),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, typeSystem.Int32)),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, new ArrayType(CreateFunctionPointer(typeSystem.Int32)))),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, new ByReferenceType(CreateFunctionPointer(typeSystem.Int32)))),
                table.GetOrCreateSignatureId(CreateMethod(CreateFunctionPointer(typeSystem.Int32))),
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32)),
            };

            CollectionAssert.AllItemsAreUnique(ids);

            // same function pointer types produce the same signature
            Assert.AreEqual(
                ids[0],
                table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, CreateFunctionPointer(typeSystem.Int32))));
        }

        [TestMethod]
        public void MethodSignature_NestedFunctionPointers_AreDistinct()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            // static void Foo(delegate*<delegate*<int>, void>) vs static void Foo(delegate*<delegate*<long>, void>)
            ushort intId = table.GetOrCreateSignatureId(
                CreateMethod(typeSystem.Void, CreateFunctionPointer(typeSystem.Void, CreateFunctionPointer(typeSystem.Int32))));
            ushort longId = table.GetOrCreateSignatureId(
                CreateMethod(typeSystem.Void, CreateFunctionPointer(typeSystem.Void, CreateFunctionPointer(typeSystem.Int64))));

            Assert.AreNotEqual(intId, longId);
        }

        [TestMethod]
        public void FieldAndLocalSignatures_FunctionPointer_AreStoredAsI4()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;

            Assert.AreEqual(
                table.GetOrCreateSignatureId(new FieldReference("f", typeSystem.Int32, typeSystem.Object)),
                table.GetOrCreateSignatureId(new FieldReference("f", CreateFunctionPointer(MethodCallingConvention.Default), typeSystem.Object)));

            Assert.AreEqual(
                table.GetOrCreateSignatureId(new Mono.Collections.Generic.Collection<VariableDefinition> { new VariableDefinition(typeSystem.Int32) }),
                table.GetOrCreateSignatureId(new Mono.Collections.Generic.Collection<VariableDefinition> { new VariableDefinition(CreateFunctionPointer(MethodCallingConvention.Default)) }));
        }

        [TestMethod]
        public void GetOrCreateSignatureId_CallSite_WithFunctionPointerParameter_MatchesMethodSignature()
        {
            var table = new nanoSignaturesTable(CreateContext());
            TypeSystem typeSystem = Mscorlib.MainModule.TypeSystem;
            var inAttribute = new TypeReference("System.Runtime.InteropServices", "InAttribute", Mscorlib.MainModule, Mscorlib.MainModule);

            // delegate*<in int, void>
            FunctionPointerType inParameter = CreateFunctionPointer(
                typeSystem.Void,
                new RequiredModifierType(inAttribute, new ByReferenceType(typeSystem.Int32)));

            // static int Foo(delegate*<in int, void>)
            ushort methodSigId = table.GetOrCreateSignatureId(CreateMethod(typeSystem.Int32, inParameter));

            // calli int(delegate*<in int, void>)
            var callSite = new CallSite(typeSystem.Int32);
            callSite.Parameters.Add(new ParameterDefinition(inParameter));

            Assert.AreEqual(methodSigId, table.GetOrCreateSignatureId(callSite));
        }

        [DataRow(MethodCallingConvention.C)]
        [DataRow(MethodCallingConvention.Unmanaged)]
        [TestMethod]
        public void MethodSignature_UnmanagedFunctionPointerParameter_Throws(MethodCallingConvention callingConvention)
        {
            var table = new nanoSignaturesTable(CreateContext());

            Assert.ThrowsException<NotSupportedException>(
                () => table.GetOrCreateSignatureId(CreateMethod(Mscorlib.MainModule.TypeSystem.Void, CreateFunctionPointer(callingConvention))));
        }
    }
}
