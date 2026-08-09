// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.Text;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Converts a configuration string into the CLR type a binding property expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three strategies, in the order that puts the ones a trimmer can follow first.
    /// </para>
    /// <para>
    /// A closed set of BCL types is converted by hand. That set is everything <c>ConfigurationBinder</c>
    /// would have sent through a <see cref="System.ComponentModel.TypeConverter"/> - numbers, enums,
    /// <see cref="TimeSpan"/>, <see cref="Uri"/> - and doing it directly is what keeps
    /// <c>TypeDescriptor.GetConverter</c>, which is <c>[RequiresUnreferencedCode]</c> upstream, off the
    /// common path entirely.
    /// </para>
    /// <para>
    /// Then the generated <see cref="ConfiguredType.Parse"/>, which covers what the hand written set cannot
    /// know about: a type carrying its own <c>[TypeConverter]</c>, and the vocabulary types whose values
    /// are public static members on the type itself.
    /// </para>
    /// <para>
    /// Then <see cref="ReflectionFallback"/>, which does both of those by reflection and neither of them
    /// under NativeAOT.
    /// </para>
    /// </remarks>
    internal sealed class ConfigurationValueConverter
    {
        private readonly ConfiguredTypeProvider _types;

        public ConfigurationValueConverter(ConfiguredTypeProvider types)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
        }

        public object Convert(string value, Type targetType, string configurationPath)
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlyingType != targetType && string.IsNullOrEmpty(value))
            {
                return null;
            }

            try
            {
                if (TryConvertWellKnown(value, underlyingType, out object converted))
                {
                    return converted;
                }
            }
            catch (Exception ex) when (!(ex is BindingConfigurationException))
            {
                throw new BindingConfigurationException(
                    $"Failed to convert '{value}' to {underlyingType.Name} (configuration path '{configurationPath}').",
                    ex);
            }

            Func<string, object> parse = _types.Context?.GetConfiguredType(underlyingType)?.Parse;
            if (parse != null)
            {
                try
                {
                    return parse(value);
                }
                catch (Exception ex) when (!(ex is BindingConfigurationException))
                {
                    throw new BindingConfigurationException(
                        $"Failed to convert '{value}' to {underlyingType.Name} (configuration path '{configurationPath}').",
                        ex);
                }
            }

            if (_types.RequireGeneratedMetadata)
            {
                throw new BindingConfigurationException(
                    $"No conversion from '{value}' to {underlyingType.Name} is available on a runtime that " +
                    $"does not support dynamic code (configuration path '{configurationPath}'). Add " +
                    $"[ServiceModelConfigurable(typeof({underlyingType.Name}))] to the service model " +
                    $"configuration context.");
            }

            if (ReflectionFallback.TryConvert(value, underlyingType, configurationPath, out object result))
            {
                return result;
            }

            throw new BindingConfigurationException(
                $"No conversion from '{value}' to {underlyingType.Name} is available, and {underlyingType.Name} " +
                $"declares no public static member with that name (configuration path '{configurationPath}').");
        }

        /// <summary>
        /// Converts the BCL types configuration actually uses, without a <c>TypeConverter</c>.
        /// </summary>
        /// <remarks>
        /// Each case matches what the corresponding <c>TypeConverter</c> would have done for an invariant
        /// culture string, with one known narrowing: the numeric converters also accept a <c>0x</c> or
        /// <c>#</c> prefixed hexadecimal literal, which these do not. No binding property is configured
        /// that way.
        /// </remarks>
        private static bool TryConvertWellKnown(string value, Type type, out object result)
        {
            if (type.IsEnum)
            {
                // Enum.Parse is not RequiresDynamicCode, and the trimmer keeps an enum's fields whenever it
                // keeps the enum, so this needs nothing generated to work under AOT.
                result = Enum.Parse(type, value, ignoreCase: true);
                return true;
            }

            CultureInfo culture = CultureInfo.InvariantCulture;

            if (type == typeof(bool)) { result = bool.Parse(value); return true; }
            if (type == typeof(int)) { result = int.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(long)) { result = long.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(TimeSpan)) { result = TimeSpan.Parse(value, culture); return true; }
            if (type == typeof(Uri)) { result = new Uri(value, UriKind.RelativeOrAbsolute); return true; }
            if (type == typeof(Encoding)) { return TryConvertEncoding(value, out result); }
            if (type == typeof(double)) { result = double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture); return true; }
            if (type == typeof(decimal)) { result = decimal.Parse(value, NumberStyles.Number, culture); return true; }
            if (type == typeof(float)) { result = float.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture); return true; }
            if (type == typeof(short)) { result = short.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(ushort)) { result = ushort.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(uint)) { result = uint.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(ulong)) { result = ulong.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(byte)) { result = byte.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(sbyte)) { result = sbyte.Parse(value, NumberStyles.Integer, culture); return true; }
            if (type == typeof(char)) { result = char.Parse(value); return true; }
            if (type == typeof(Guid)) { result = Guid.Parse(value); return true; }
            if (type == typeof(DateTime)) { result = DateTime.Parse(value, culture, DateTimeStyles.None); return true; }
            if (type == typeof(DateTimeOffset)) { result = DateTimeOffset.Parse(value, culture, DateTimeStyles.None); return true; }

            result = null;
            return false;
        }

        private static bool TryConvertEncoding(string value, out object encoding)
        {
            try
            {
                encoding = Encoding.GetEncoding(value);
                return true;
            }
            catch (ArgumentException)
            {
                // Not a code page name. A generated Parse, or a static member lookup, may still resolve it -
                // "UTF8" names Encoding.UTF8 rather than a code page.
                encoding = null;
                return false;
            }
        }
    }
}
