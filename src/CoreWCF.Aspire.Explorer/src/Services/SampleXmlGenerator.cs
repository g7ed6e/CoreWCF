// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace CoreWCF.Aspire.Explorer.Services;

/// <summary>
/// Produces a best-effort sample XML instance for a global schema element, used to pre-fill the SOAP
/// body of a request so an operation can be invoked without hand-writing the payload from scratch.
/// </summary>
public sealed class SampleXmlGenerator
{
    private const int MaxDepth = 6;

    private readonly XmlSchemaSet _schemas;

    public SampleXmlGenerator(XmlSchemaSet schemas)
    {
        _schemas = schemas;
        if (!_schemas.IsCompiled)
        {
            try
            {
                _schemas.Compile();
            }
            catch (XmlSchemaException)
            {
                // Best effort: an incomplete/invalid schema set still lets us emit the wrapper element.
            }
        }
    }

    /// <summary>Generates a sample element for the named global element, or <see langword="null"/> if unknown.</summary>
    public XElement? Generate(XmlQualifiedName elementName)
    {
        var element = FindGlobalElement(elementName);
        return element is null ? null : BuildElement(element, 0, new HashSet<XmlQualifiedName>());
    }

    private XmlSchemaElement? FindGlobalElement(XmlQualifiedName name)
    {
        if (_schemas.GlobalElements[name] is XmlSchemaElement element)
        {
            return element;
        }

        foreach (XmlSchemaElement candidate in _schemas.GlobalElements.Values)
        {
            if (candidate.QualifiedName == name)
            {
                return candidate;
            }
        }

        return null;
    }

    private XElement BuildElement(XmlSchemaElement element, int depth, HashSet<XmlQualifiedName> visited)
    {
        var qName = element.QualifiedName;
        var node = new XElement(XName.Get(qName.Name, qName.Namespace));

        if (depth >= MaxDepth)
        {
            return node;
        }

        var schemaType = element.ElementSchemaType;
        switch (schemaType)
        {
            case XmlSchemaSimpleType simple:
                node.Value = PlaceholderFor(simple.Datatype);
                break;
            case XmlSchemaComplexType complex:
                PopulateComplex(node, complex, depth, visited);
                break;
            default:
                node.Value = string.Empty;
                break;
        }

        return node;
    }

    private void PopulateComplex(XElement node, XmlSchemaComplexType complex, int depth, HashSet<XmlQualifiedName> visited)
    {
        if (complex.QualifiedName is { IsEmpty: false } typeName)
        {
            if (!visited.Add(typeName))
            {
                return; // recursion guard
            }
        }

        if (complex.ContentType == XmlSchemaContentType.TextOnly && complex.Datatype is { } builtIn)
        {
            // simpleContent: a value with optional attributes.
            node.Value = PlaceholderFor(builtIn);
        }
        else
        {
            AppendParticle(node, complex.ContentTypeParticle, depth, visited);
        }

        if (complex.QualifiedName is { IsEmpty: false } tn)
        {
            visited.Remove(tn);
        }
    }

    private void AppendParticle(XElement node, XmlSchemaParticle? particle, int depth, HashSet<XmlQualifiedName> visited)
    {
        switch (particle)
        {
            case XmlSchemaElement childElement:
                var resolved = ResolveElement(childElement);
                if (resolved is not null)
                {
                    node.Add(BuildElement(resolved, depth + 1, visited));
                }

                break;
            case XmlSchemaGroupBase group: // sequence, choice, all
                foreach (XmlSchemaObject item in group.Items)
                {
                    if (item is XmlSchemaParticle childParticle)
                    {
                        AppendParticle(node, childParticle, depth, visited);
                    }

                    if (group is XmlSchemaChoice)
                    {
                        break; // one branch is enough for a sample
                    }
                }

                break;
        }
    }

    private XmlSchemaElement? ResolveElement(XmlSchemaElement element)
    {
        if (!element.RefName.IsEmpty)
        {
            return _schemas.GlobalElements[element.RefName] as XmlSchemaElement;
        }

        return element;
    }

    private static string PlaceholderFor(XmlSchemaDatatype? datatype)
    {
        if (datatype is null)
        {
            return string.Empty;
        }

        return datatype.TypeCode switch
        {
            XmlTypeCode.Boolean => "false",
            XmlTypeCode.Decimal or XmlTypeCode.Float or XmlTypeCode.Double
                or XmlTypeCode.Integer or XmlTypeCode.Int or XmlTypeCode.Long
                or XmlTypeCode.Short or XmlTypeCode.Byte
                or XmlTypeCode.NonNegativeInteger or XmlTypeCode.PositiveInteger
                or XmlTypeCode.UnsignedInt or XmlTypeCode.UnsignedLong
                or XmlTypeCode.UnsignedShort or XmlTypeCode.UnsignedByte => "0",
            XmlTypeCode.DateTime => "2020-01-01T00:00:00",
            XmlTypeCode.Date => "2020-01-01",
            XmlTypeCode.Time => "00:00:00",
            _ => "string",
        };
    }
}
