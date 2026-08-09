// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Every reflection call this package makes, in one file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here survives trimming or NativeAOT, and none of it can be annotated into safety: a type
    /// named by a configuration string is a type nothing references, so the trimmer removes it before the
    /// string is ever read. The source generator exists to make all of this unnecessary - see
    /// <see cref="ServiceModelConfigurationContext"/> - and this file is what runs when no context covers
    /// a type.
    /// </para>
    /// <para>
    /// It is a single file on purpose. The library targets netstandard2.0, where
    /// <c>RequiresUnreferencedCodeAttribute</c> does not exist and <c>EnableTrimAnalyzer</c> does nothing,
    /// so no build gate will report that a new reflection call has appeared somewhere else. Keeping them
    /// together means the question "did the generated path stay reflection free?" is answered by reading
    /// one file rather than by trusting a warning that is never emitted.
    /// </para>
    /// <para>
    /// The trim and AOT failure modes it carries, for the record: <c>Type.GetType</c> is IL2057,
    /// <c>Activator.CreateInstance</c> IL2067, <c>GetProperty</c>/<c>GetValue</c>/<c>SetValue</c> IL2075,
    /// <c>TypeDescriptor.GetConverter</c> IL2026, and <c>MakeGenericType</c> over a value type IL3050 -
    /// the last being a hard failure under AOT rather than a warning.
    /// </para>
    /// </remarks>
    internal static class ReflectionFallback
    {
        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        private const BindingFlags StaticMemberFlags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;

        /// <summary>
        /// Loads the type an assembly qualified name identifies, or null.
        /// </summary>
        public static Type ResolveType(string name) => Type.GetType(name, throwOnError: false, ignoreCase: false);

        /// <summary>
        /// Describes <paramref name="type"/> by reflecting over it, in the shape the generator would have
        /// emitted.
        /// </summary>
        public static ConfiguredType Describe(Type type)
        {
            Action<object, object> addItem = DescribeCollectionAdd(type, out Type itemType);

            return new ConfiguredType(
                type,
                create: DescribeConstructor(type),
                members: DescribeMembers(type),
                parse: null,
                addItem: addItem,
                itemType: itemType);
        }

        private static Func<object> DescribeConstructor(Type type)
        {
            if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
            {
                return null;
            }

            return () => Activator.CreateInstance(type);
        }

        private static IReadOnlyDictionary<string, ConfiguredMember> DescribeMembers(Type type)
        {
            var members = new Dictionary<string, ConfiguredMember>(StringComparer.OrdinalIgnoreCase);

            // Walked most derived first, so a property re-declared with 'new' shadows the one it hides.
            // Type.GetProperty reports that pair as an AmbiguousMatchException instead of choosing.
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (PropertyInfo property in current.GetProperties(MemberFlags | BindingFlags.DeclaredOnly))
                {
                    if (property.GetIndexParameters().Length != 0 || members.ContainsKey(property.Name))
                    {
                        continue;
                    }

                    members[property.Name] = DescribeMember(property);
                }
            }

            return members;
        }

        private static ConfiguredMember DescribeMember(PropertyInfo property)
        {
            Func<object, object> get = property.GetMethod != null && property.GetMethod.IsPublic
                ? instance => property.GetValue(instance)
                : (Func<object, object>)null;

            Action<object, object> set = property.SetMethod != null && property.SetMethod.IsPublic
                ? (instance, value) => property.SetValue(instance, value)
                : (Action<object, object>)null;

            return new ConfiguredMember(property.Name, property.PropertyType, get, set);
        }

        private static Action<object, object> DescribeCollectionAdd(Type type, out Type itemType)
        {
            itemType = null;

            if (type == typeof(string) || !TryGetCollectionItemType(type, out itemType))
            {
                return null;
            }

            // System.Collections.IList rather than ICollection of T reached through MakeGenericType. Every
            // collection a binding exposes derives from Collection of T, which implements IList explicitly
            // and type checks the item on the way through. MakeGenericType over a value type element is
            // IL3050 - a hard failure under AOT rather than a warning - and this costs nothing to avoid.
            if (typeof(IList).IsAssignableFrom(type))
            {
                return (collection, item) => ((IList)collection).Add(item);
            }

            MethodInfo add = typeof(ICollection<>).MakeGenericType(itemType).GetMethod("Add");
            return (collection, item) => add.Invoke(collection, new[] { item });
        }

        private static bool TryGetCollectionItemType(Type type, out Type itemType)
        {
            itemType = null;

            foreach (Type contract in type.GetInterfaces())
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICollection<>))
                {
                    itemType = contract.GetGenericArguments()[0];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Converts a configuration string to <paramref name="targetType"/> using the type's own
        /// <see cref="TypeConverter"/>, or failing that a public static member of that name.
        /// </summary>
        /// <remarks>
        /// The static member lookup is what covers the types with no converter at all: <c>MessageVersion</c>,
        /// <c>EnvelopeVersion</c>, <c>SecurityAlgorithmSuite</c>, <c>MessageSecurityVersion</c>. They share
        /// a trait that makes a hand written converter per type unnecessary - their well known values are
        /// public static members on the type itself, as in <c>MessageVersion.Soap12WSAddressing10</c>. One
        /// lookup replaces the whole set, and keeps working for types added later.
        /// </remarks>
        public static bool TryConvert(string value, Type targetType, string configurationPath, out object result)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(typeof(string)))
            {
                try
                {
                    result = converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
                    return true;
                }
                catch (Exception ex)
                {
                    throw new BindingConfigurationException(
                        $"Failed to convert '{value}' to {targetType.Name} (configuration path '{configurationPath}').",
                        ex);
                }
            }

            return TryResolveWellKnownValue(targetType, value, out result);
        }

        /// <summary>
        /// Resolves a public static property or field of <paramref name="type"/> named
        /// <paramref name="name"/> whose value is assignable to <paramref name="type"/>.
        /// </summary>
        private static bool TryResolveWellKnownValue(Type type, string name, out object value)
        {
            value = null;

            PropertyInfo property = type.GetProperty(name, StaticMemberFlags);
            if (property != null && property.CanRead && type.IsAssignableFrom(property.PropertyType))
            {
                value = property.GetValue(null);
                return value != null;
            }

            FieldInfo field = type.GetField(name, StaticMemberFlags);
            if (field != null && type.IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(null);
                return value != null;
            }

            return false;
        }
    }
}
