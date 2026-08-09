// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Configuration;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Binds a configuration section onto an existing object graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hydration cannot be layered on <c>ConfigurationBinder</c>, for two reasons it has no extensibility
    /// point for. A binding and its elements are polymorphic, and <c>ConfigurationBinder</c> cannot pick a
    /// concrete type from a discriminator, so neither the binding itself nor the contents of
    /// <c>CustomBinding.Elements</c> can be created. And several types that appear in ordinary binding
    /// configuration have no <see cref="System.ComponentModel.TypeConverter"/> at all -
    /// <c>MessageVersion</c>, <c>EnvelopeVersion</c>, <c>SecurityAlgorithmSuite</c>,
    /// <c>MessageSecurityVersion</c> - so their values cannot be converted from a string. See
    /// <see cref="ConfigurationValueConverter"/>.
    /// </para>
    /// <para>
    /// Driving the traversal from the configuration keys rather than from the target's properties buys two
    /// further things. An unknown key becomes an error naming its configuration path, instead of a value
    /// that silently does nothing. And a property the configuration does not mention is genuinely
    /// untouched: <c>ConfigurationBinder</c> round-trips every settable property through its getter and
    /// setter whether or not it is configured, which matters here because binding accessors are not pure -
    /// <c>ReaderQuotas</c> copies the incoming value over the encoder's instance, and <c>Security</c>
    /// rejects null and replaces the whole sub-object.
    /// </para>
    /// <para>
    /// How a member is reached is <see cref="ConfiguredTypeProvider"/>'s business, not this class's. The
    /// traversal is written once and runs unchanged whether the members came from the generator or from
    /// reflection.
    /// </para>
    /// </remarks>
    internal sealed class ConfigurationObjectBinder
    {
        private readonly ServiceModelTypeResolver _resolver;
        private readonly ConfiguredTypeProvider _types;
        private readonly ConfigurationValueConverter _converter;
        private readonly string _discriminatorKey;

        public ConfigurationObjectBinder(
            ServiceModelTypeResolver resolver,
            ConfiguredTypeProvider types,
            string discriminatorKey)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _discriminatorKey = discriminatorKey ?? throw new ArgumentNullException(nameof(discriminatorKey));
            _converter = new ConfigurationValueConverter(types);
        }

        public void Bind(object instance, IConfiguration section)
        {
            ConfiguredType configured = _types.Get(instance.GetType(), (section as IConfigurationSection)?.Path);

            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (string.Equals(child.Key, _discriminatorKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!configured.Members.TryGetValue(child.Key, out ConfiguredMember member))
                {
                    throw new BindingConfigurationException(
                        $"'{configured.Type.Name}' has no property named '{child.Key}' " +
                        $"(configuration path '{child.Path}').");
                }

                BindMember(instance, configured, member, child);
            }
        }

        private void BindMember(
            object instance,
            ConfiguredType declaring,
            ConfiguredMember member,
            IConfigurationSection section)
        {
            if (section.Value != null)
            {
                RequireSetter(declaring, member, section);
                member.Set(instance, _converter.Convert(section.Value, member.MemberType, section.Path));
                return;
            }

            string typeName = section[_discriminatorKey];
            if (typeName != null || member.MemberType.IsAbstract)
            {
                // The configuration chose the concrete type, so a fresh instance has to replace whatever is
                // there. Asked before anything else because the declared type may be abstract, and an
                // abstract type is one nothing needs metadata for.
                RequireSetter(declaring, member, section);
                object replacement = CreateInstance(member.MemberType, typeName, section);
                Bind(replacement, section);
                member.Set(instance, replacement);
                return;
            }

            ConfiguredType memberType = _types.Get(member.MemberType, section.Path);

            if (memberType.AddItem != null)
            {
                object collection = member.Get?.Invoke(instance);
                if (collection == null)
                {
                    throw new BindingConfigurationException(
                        $"'{declaring.Type.Name}.{member.Name}' is null, so its items cannot be populated " +
                        $"(configuration path '{section.Path}').");
                }

                BindCollection(collection, memberType, section);
                return;
            }

            // Bind into the instance the binding already created. Bindings carry meaningful defaults on these
            // sub-objects (security, reader quotas), and replacing them would silently discard those defaults.
            object existing = member.Get?.Invoke(instance);
            if (existing != null)
            {
                Bind(existing, section);
                return;
            }

            RequireSetter(declaring, member, section);
            object created = CreateInstance(member.MemberType, typeName: null, section: section);
            Bind(created, section);
            member.Set(instance, created);
        }

        private void BindCollection(object collection, ConfiguredType collectionType, IConfigurationSection section)
        {
            foreach (IConfigurationSection child in section.GetChildren())
            {
                object item = CreateInstance(collectionType.ItemType, child[_discriminatorKey], child);
                Bind(item, child);
                collectionType.AddItem(collection, item);
            }
        }

        /// <summary>
        /// Creates an instance of a type the configuration has already named.
        /// </summary>
        public object CreateInstance(Type concreteType, IConfigurationSection section)
        {
            if (concreteType.IsAbstract)
            {
                throw new BindingConfigurationException(
                    $"'{concreteType.FullName}' is abstract and cannot be created from configuration " +
                    $"(configuration path '{section.Path}').");
            }

            ConfiguredType configured = _types.Get(concreteType, section.Path);
            if (configured.Create == null)
            {
                throw new BindingConfigurationException(
                    $"'{concreteType.FullName}' has no parameterless constructor and cannot be created from " +
                    $"configuration (configuration path '{section.Path}').");
            }

            return configured.Create();
        }

        private object CreateInstance(Type declaredType, string typeName, IConfigurationSection section)
        {
            if (typeName != null)
            {
                return CreateInstance(_resolver.Resolve(declaredType, typeName, section.Path), section);
            }

            if (declaredType.IsAbstract)
            {
                throw new BindingConfigurationException(
                    $"A '{_discriminatorKey}' value is required to choose a concrete {declaredType.Name} " +
                    $"(configuration path '{section.Path}').");
            }

            return CreateInstance(declaredType, section);
        }

        private static void RequireSetter(ConfiguredType declaring, ConfiguredMember member, IConfigurationSection section)
        {
            if (member.Set == null)
            {
                throw new BindingConfigurationException(
                    $"'{declaring.Type.Name}.{member.Name}' is read-only and cannot be set from configuration " +
                    $"(configuration path '{section.Path}').");
            }
        }
    }
}
