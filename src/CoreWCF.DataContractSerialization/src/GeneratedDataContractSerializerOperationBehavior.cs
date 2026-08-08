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
            // knownTypes is not consulted yet: the first generator slice emits no polymorphic
            // output, so a contract needing [KnownType] resolution has no generated serializer and
            // this returns null, taking the reflection path. When i:type emission lands, the
            // generated serializer will need these to resolve derived types.
            return _context.GetSerializer(type, name, ns);
        }
    }
}
