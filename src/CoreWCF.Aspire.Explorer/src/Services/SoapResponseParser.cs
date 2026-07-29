// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CoreWCF.Aspire.Explorer.Services;

/// <summary>A single row of the formatted response grid.</summary>
public sealed record SoapResponseRow(string Name, string Value);

/// <summary>A parsed SOAP response: either result rows or a fault.</summary>
public sealed record ParsedSoapResponse(bool IsFault, string? FaultText, IReadOnlyList<SoapResponseRow> Rows);

/// <summary>
/// Flattens a SOAP response body into Name / Value rows for the "Formatted" response view, and surfaces
/// SOAP faults. Mirrors the response grid of the classic WCF Test Client.
/// </summary>
public static class SoapResponseParser
{
    public static ParsedSoapResponse Parse(string xml)
    {
        var empty = new List<SoapResponseRow>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new ParsedSoapResponse(false, null, empty);
        }

        try
        {
            var document = XDocument.Parse(xml);
            var body = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
            var wrapper = body?.Elements().FirstOrDefault();
            if (wrapper is null)
            {
                return new ParsedSoapResponse(false, null, empty);
            }

            if (wrapper.Name.LocalName == "Fault")
            {
                var faultText = wrapper.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName is "faultstring" or "Text")?.Value ?? wrapper.Value;
                return new ParsedSoapResponse(true, faultText, empty);
            }

            var rows = new List<SoapResponseRow>();
            foreach (var child in wrapper.Elements())
            {
                rows.Add(new SoapResponseRow(child.Name.LocalName, child.HasElements ? child.ToString() : child.Value));
            }

            if (rows.Count == 0)
            {
                rows.Add(new SoapResponseRow(wrapper.Name.LocalName, wrapper.Value));
            }

            return new ParsedSoapResponse(false, null, rows);
        }
        catch (XmlException)
        {
            return new ParsedSoapResponse(false, null, empty);
        }
    }
}
