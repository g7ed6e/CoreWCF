// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// One settable or readable member of a <see cref="ConfiguredType"/>, reached through delegates the
    /// source generator compiled rather than through <see cref="System.Reflection.PropertyInfo"/>.
    /// </summary>
    public sealed class ConfiguredMember
    {
        public ConfiguredMember(string name, Type memberType, Func<object, object> get, Action<object, object> set)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MemberType = memberType ?? throw new ArgumentNullException(nameof(memberType));
            Get = get;
            Set = set;
        }

        /// <summary>The member's name as declared, used to report errors in the configured spelling.</summary>
        public string Name { get; }

        /// <summary>The member's declared type.</summary>
        public Type MemberType { get; }

        /// <summary>
        /// Reads the member, or <see langword="null"/> when it has no getter. The binder needs this to bind
        /// into the sub-object a binding already created rather than replacing it, which is what preserves
        /// the defaults a binding puts on its security and reader quota objects.
        /// </summary>
        public Func<object, object> Get { get; }

        /// <summary>
        /// Writes the member, or <see langword="null"/> when it is read-only.
        /// </summary>
        public Action<object, object> Set { get; }
    }
}
