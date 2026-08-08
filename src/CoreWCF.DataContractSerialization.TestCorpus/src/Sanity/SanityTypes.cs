// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CoreWCF.DataContractSerialization.TestCorpus.Sanity
{
    // CoreWCF-owned contract types covering the capability surface the source generator must
    // reproduce. These exist so the harness can be proven end-to-end before any upstream
    // dotnet/runtime code is imported, separating "is the harness correct?" from "does upstream
    // code compile here?".
    //
    // Every instance must be deterministic: no DateTime.Now, Guid.NewGuid, Random, or
    // DateTimeKind.Local. Local kind is serialized with the machine's UTC offset, so a fixture
    // generated in one time zone would fail on a UTC build agent - and re-running locally would
    // never reveal it.

    /// <summary>
    /// Every primitive the generator must format via XmlConvert, as a mix of properties and
    /// public fields. Fields are pervasive in DataContract code and are a classic generator
    /// oversight.
    /// </summary>
    /// <remarks>
    /// The double and float members are the deliberate .NET Framework divergence canary:
    /// .NET Core 3.0 made floating-point ToString shortest-round-trippable, so net472 is expected
    /// to render some values differently and exercise the per-TFM fixture override mechanism.
    /// </remarks>
    [DataContract]
    public class SanityPrimitives
    {
        [DataMember]
        public bool BoolMember;

        [DataMember]
        public byte ByteMember;

        [DataMember]
        public char CharMember;

        [DataMember]
        public int IntMember { get; set; }

        [DataMember]
        public long LongMember { get; set; }

        [DataMember]
        public short ShortMember { get; set; }

        [DataMember]
        public decimal DecimalMember { get; set; }

        [DataMember]
        public double DoubleMember { get; set; }

        [DataMember]
        public float FloatMember { get; set; }

        [DataMember]
        public string StringMember { get; set; }

        [DataMember]
        public Guid GuidMember { get; set; }

        [DataMember]
        public DateTime DateTimeMember { get; set; }

        [DataMember]
        public TimeSpan TimeSpanMember { get; set; }

        [DataMember]
        public byte[] BytesMember { get; set; }

        public static SanityPrimitives Populated()
        {
            return new SanityPrimitives
            {
                BoolMember = true,
                ByteMember = 200,
                CharMember = 'Z',
                IntMember = -42,
                LongMember = 9007199254740993L,
                ShortMember = 1234,
                DecimalMember = 12345.6789m,
                DoubleMember = 0.1 + 0.2,
                FloatMember = 1.1E-5f,
                StringMember = "hello <world> & \"friends\"",
                GuidMember = new Guid("2f9e4c1a-0000-4000-8000-000000000001"),
                DateTimeMember = new DateTime(2020, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
                TimeSpanMember = new TimeSpan(1, 2, 3, 4, 5),
                BytesMember = new byte[] { 0, 1, 2, 250, 255 }
            };
        }
    }

    /// <summary>
    /// Member ordering and emission rules: explicit Order, renamed members, required members, and
    /// suppression of defaults. Historically the single largest source of hand-written serializer
    /// bugs.
    /// </summary>
    [DataContract]
    public class SanityMemberAttributes
    {
        [DataMember(Order = 3)]
        public string Third { get; set; }

        [DataMember(Order = 1)]
        public string First { get; set; }

        [DataMember(Order = 2, Name = "RenamedSecond")]
        public string Second { get; set; }

        [DataMember(IsRequired = true)]
        public int Required { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int OmittedWhenDefault { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string OmittedStringWhenNull { get; set; }

        public string NotAMember { get; set; }

        public static SanityMemberAttributes Populated()
        {
            return new SanityMemberAttributes
            {
                First = "1",
                Second = "2",
                Third = "3",
                Required = 7,
                OmittedWhenDefault = 0,
                OmittedStringWhenNull = null,
                NotAMember = "must not appear"
            };
        }
    }

    /// <summary>Custom contract name and namespace, driving xmlns declaration and prefix assignment.</summary>
    [DataContract(Name = "RenamedContract", Namespace = "http://corewcf.example/sanity")]
    public class SanityCustomNaming
    {
        [DataMember(Name = "RenamedValue")]
        public string Value { get; set; }

        [DataMember]
        public SanityNestedNamespace Nested { get; set; }

        public static SanityCustomNaming Populated()
        {
            return new SanityCustomNaming
            {
                Value = "outer",
                Nested = new SanityNestedNamespace { Inner = "inner" }
            };
        }
    }

    /// <summary>A member type in a different contract namespace, to pin namespace inference through the graph.</summary>
    [DataContract(Namespace = "http://corewcf.example/sanity/nested")]
    public class SanityNestedNamespace
    {
        [DataMember]
        public string Inner { get; set; }
    }

    /// <summary>Base of the inheritance pair. Base members are emitted before derived members.</summary>
    [DataContract]
    [KnownType(typeof(SanityDerived))]
    public class SanityBase
    {
        [DataMember]
        public string BaseMember { get; set; }

        [DataMember]
        public int BaseOrdinal { get; set; }
    }

    /// <summary>Derived contract. Serialized through the base declared type it emits an i:type attribute.</summary>
    [DataContract]
    public class SanityDerived : SanityBase
    {
        [DataMember]
        public string DerivedMember { get; set; }

        public static SanityDerived Populated()
        {
            return new SanityDerived
            {
                BaseMember = "base",
                BaseOrdinal = 1,
                DerivedMember = "derived"
            };
        }
    }

    /// <summary>Holder whose member is declared as the base type but holds a derived instance.</summary>
    [DataContract]
    public class SanityKnownTypeHolder
    {
        [DataMember]
        public SanityBase Value { get; set; }

        public static SanityKnownTypeHolder Populated()
        {
            return new SanityKnownTypeHolder { Value = SanityDerived.Populated() };
        }
    }

    /// <summary>Arrays, lists and dictionaries, whose element naming rules no generator gets right by accident.</summary>
    [DataContract]
    public class SanityCollections
    {
        [DataMember]
        public string[] StringArray { get; set; }

        [DataMember]
        public List<int> IntList { get; set; }

        [DataMember]
        public Dictionary<string, string> StringMap { get; set; }

        [DataMember]
        public int[] EmptyArray { get; set; }

        public static SanityCollections Populated()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            map.Add("alpha", "1");

            return new SanityCollections
            {
                StringArray = new string[] { "a", "b" },
                IntList = new List<int> { 1, 2, 3 },
                StringMap = map,
                EmptyArray = new int[0]
            };
        }
    }

    public enum SanityEnum
    {
        None = 0,
        First = 1,
        Second = 2
    }

    [Flags]
    public enum SanityFlagsEnum
    {
        None = 0,
        Alpha = 1,
        Beta = 2,
        Gamma = 4
    }

    [DataContract]
    public enum SanityRenamedEnum
    {
        [EnumMember(Value = "wire-none")]
        None = 0,

        [EnumMember(Value = "wire-one")]
        One = 1
    }

    /// <summary>Enum value to wire-text mapping, including EnumMember renaming.</summary>
    [DataContract]
    public class SanityEnums
    {
        [DataMember]
        public SanityEnum Plain { get; set; }

        [DataMember]
        public SanityFlagsEnum Flags { get; set; }

        [DataMember]
        public SanityRenamedEnum Renamed { get; set; }

        public static SanityEnums Populated()
        {
            return new SanityEnums
            {
                Plain = SanityEnum.Second,
                Flags = SanityFlagsEnum.Alpha | SanityFlagsEnum.Gamma,
                Renamed = SanityRenamedEnum.One
            };
        }
    }

    /// <summary>Null references and nullable value types, driving i:nil emission.</summary>
    [DataContract]
    public class SanityNullable
    {
        [DataMember]
        public string NullString { get; set; }

        [DataMember]
        public SanityNestedNamespace NullReference { get; set; }

        [DataMember]
        public int? NullInt { get; set; }

        [DataMember]
        public int? SetInt { get; set; }

        [DataMember]
        public DateTime? NullDateTime { get; set; }

        public static SanityNullable Populated()
        {
            return new SanityNullable
            {
                NullString = null,
                NullReference = null,
                NullInt = null,
                SetInt = 5,
                NullDateTime = null
            };
        }
    }

    /// <summary>
    /// Reference-preserving contract containing a cycle, driving z:Id / z:Ref emission and the
    /// serialization namespace declaration. The hardest single behaviour for a generator to match.
    /// </summary>
    [DataContract(IsReference = true)]
    public class SanityReferenceNode
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public SanityReferenceNode Next { get; set; }

        [DataMember]
        public SanityReferenceNode Self { get; set; }

        public static SanityReferenceNode Populated()
        {
            SanityReferenceNode first = new SanityReferenceNode { Name = "first" };
            SanityReferenceNode second = new SanityReferenceNode { Name = "second" };

            first.Next = second;
            first.Self = first;
            second.Next = first;
            second.Self = second;

            return first;
        }
    }
}
