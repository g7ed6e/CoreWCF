// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Everything hydrating a type from configuration needs, as delegates the source generator compiled.
    /// </summary>
    /// <remarks>
    /// Each member replaces one reflection call the trimmer cannot follow and, in the collection case, one
    /// the NativeAOT compiler cannot execute at all:
    /// <list type="bullet">
    /// <item><see cref="Create"/> replaces <see cref="Activator.CreateInstance(Type)"/>.</item>
    /// <item><see cref="Members"/> replaces <c>Type.GetProperty</c> plus <c>PropertyInfo.GetValue/SetValue</c>.</item>
    /// <item><see cref="AddItem"/> replaces <c>typeof(ICollection&lt;&gt;).MakeGenericType(...).GetMethod("Add").Invoke(...)</c>.</item>
    /// <item><see cref="Parse"/> replaces <c>TypeDescriptor.GetConverter</c> and the static member lookup beside it.</item>
    /// </list>
    /// A null member means the generator found no way to do that thing for this type, not that the type is
    /// unsupported: the binder falls back to reflection for exactly that operation.
    /// </remarks>
    public sealed class ConfiguredType
    {
        private static readonly IReadOnlyDictionary<string, ConfiguredMember> s_noMembers =
            new Dictionary<string, ConfiguredMember>(0, StringComparer.OrdinalIgnoreCase);

        public ConfiguredType(
            Type type,
            Func<object> create = null,
            IReadOnlyDictionary<string, ConfiguredMember> members = null,
            Func<string, object> parse = null,
            Action<object, object> addItem = null,
            Type itemType = null)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Create = create;
            Members = members ?? s_noMembers;
            Parse = parse;
            AddItem = addItem;
            ItemType = itemType;
        }

        /// <summary>The type this metadata describes.</summary>
        public Type Type { get; }

        /// <summary>
        /// Creates an instance, or <see langword="null"/> when the type has no accessible parameterless
        /// constructor.
        /// </summary>
        public Func<object> Create { get; }

        /// <summary>
        /// The members configuration may set, keyed case-insensitively to match how configuration keys are
        /// compared elsewhere.
        /// </summary>
        public IReadOnlyDictionary<string, ConfiguredMember> Members { get; }

        /// <summary>
        /// Converts a configuration string to an instance, or <see langword="null"/> when the type is not
        /// something a single value can express.
        /// </summary>
        public Func<string, object> Parse { get; }

        /// <summary>
        /// Appends an item to an instance of this type, or <see langword="null"/> when it is not a
        /// collection. The generator emits a closed generic cast, so no generic type is constructed at run
        /// time.
        /// </summary>
        public Action<object, object> AddItem { get; }

        /// <summary>
        /// The element type when <see cref="AddItem"/> is set, otherwise <see langword="null"/>.
        /// </summary>
        public Type ItemType { get; }
    }
}
