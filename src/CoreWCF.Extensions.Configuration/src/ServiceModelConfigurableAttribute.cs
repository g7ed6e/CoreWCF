// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace CoreWCF.Extensions.Configuration
{
    /// <summary>
    /// Declares that <paramref name="type"/> participates in service model configuration, so that the
    /// source generator emits the metadata hydrating it needs and adds it to the annotated
    /// <see cref="ServiceModelConfigurationContext"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// [ServiceModelConfigurable(typeof(NetTcpBinding), Name = "netTcp")]
    /// [ServiceModelConfigurable(typeof(EchoService))]
    /// [ServiceModelConfigurable(typeof(IEchoService))]
    /// public partial class MyServiceModel : ServiceModelConfigurationContext { }
    /// </code>
    /// </example>
    /// <remarks>
    /// Modelled on <c>JsonSerializableAttribute</c>, and on <c>DataContractSerializableAttribute</c> next
    /// door. Declaring the context is the opt-in: nothing is generated for a type until something asks for
    /// it, and the resulting list is rooted with <c>typeof</c>, which is what makes the type survive
    /// trimming and what removes the <see cref="Type.GetType(string)"/> call that cannot survive it.
    /// <para>
    /// Only the type named here has to be listed. The generator walks its settable public property graph
    /// transitively, so listing a binding also covers its security, quota and reliable session objects.
    /// The types it cannot infer are the polymorphic ones - the concrete <c>BindingElement</c>s inside a
    /// <c>CustomBinding.Elements</c> list - which is why those have to be listed individually.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ServiceModelConfigurableAttribute : Attribute
    {
        public ServiceModelConfigurableAttribute(Type type)
        {
            Type = type;
        }

        /// <summary>The type configuration may name.</summary>
        public Type Type { get; }

        /// <summary>
        /// An additional, shorter name configuration may use for <see cref="Type"/>. The assembly qualified
        /// name always resolves whether or not this is set, so setting it adds a spelling rather than
        /// replacing one.
        /// </summary>
        public string Name { get; set; }
    }
}
