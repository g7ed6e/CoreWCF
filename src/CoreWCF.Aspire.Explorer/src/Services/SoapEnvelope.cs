// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using CoreWCF.Aspire.Explorer.Model;

namespace CoreWCF.Aspire.Explorer.Services;

/// <summary>Helpers for building SOAP envelopes around a body payload.</summary>
public static class SoapEnvelope
{
    public const string Soap11Namespace = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Soap12Namespace = "http://www.w3.org/2003/05/soap-envelope";

    /// <summary>Wraps a body element in a SOAP envelope for the given version.</summary>
    public static string Wrap(XElement? body, SoapVersion version)
    {
        XNamespace ns = version == SoapVersion.Soap12 ? Soap12Namespace : Soap11Namespace;
        var envelope = new XElement(
            ns + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", ns.NamespaceName),
            new XElement(ns + "Header"),
            new XElement(ns + "Body", body));

        return envelope.ToString();
    }
}
