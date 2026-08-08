// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Xml;
using CoreWCF.Description;
using CoreWCF.Runtime.Serialization;

namespace CoreWCF.DataContractSerialization
{
    /// <summary>
    /// An operation behavior that serves source-generated serializers from a
    /// <see cref="DataContractSerializerContext"/>, falling back to the reflection-based serializer
    /// for anything the context does not cover.
    /// </summary>
    /// <remarks>
    /// Replace the built-in behavior rather than adding alongside it:
    /// <c>OperationDescription.Behaviors</c> is a keyed collection where the first entry wins, so
    /// <c>Remove&lt;DataContractSerializerOperationBehavior&gt;()</c> then <c>Add(...)</c>.
    /// </remarks>
    public class GeneratedDataContractSerializerOperationBehavior : DataContractSerializerOperationBehavior
    {
        private readonly DataContractSerializerContext _context;

        public GeneratedDataContractSerializerOperationBehavior(OperationDescription operation, DataContractSerializerContext context)
            : base(operation)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public DataContractSerializerContext Context => _context;

        public override AotXmlObjectSerializer CreateAotSerializer(Type type, XmlDictionaryString name, XmlDictionaryString ns, IList<Type> knownTypes)
        {
            // Known types mean a member may hold a derived instance, which the real serializer
            // signals with an i:type attribute and the derived contract's members. Generated
            // serializers do not emit that yet, and writing the declared type's members instead
            // would produce wrong XML rather than falling back - so decline and let the
            // reflection-based serializer handle it.
            //
            // The generator makes the same judgement at compile time from [KnownType] attributes;
            // this catches the case where CoreWCF supplies known types from the operation contract
            // instead, which no attribute would reveal.
            if (knownTypes != null && knownTypes.Count > 0)
            {
                return null;
            }

            return _context.GetSerializer(type, name, ns);
        }
    }
}
