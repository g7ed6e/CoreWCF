// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using CoreWCF.Aspire.Explorer.Model;

namespace CoreWCF.Aspire.Explorer.Services;

/// <summary>The outcome of invoking a SOAP operation.</summary>
/// <param name="StatusCode">HTTP status code returned by the service.</param>
/// <param name="ReasonPhrase">HTTP reason phrase.</param>
/// <param name="Body">Response body (pretty-printed when it is valid XML).</param>
/// <param name="IsSuccess">Whether the HTTP call succeeded.</param>
/// <param name="Elapsed">Round-trip duration.</param>
public sealed record SoapInvocationResult(
    int StatusCode,
    string? ReasonPhrase,
    string Body,
    bool IsSuccess,
    TimeSpan Elapsed);

/// <summary>Sends a raw SOAP request to a service endpoint and returns the response.</summary>
public sealed class SoapInvoker(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<SoapInvocationResult> InvokeAsync(
        string endpointAddress,
        WsdlOperation operation,
        string envelopeXml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpointAddress);
        ArgumentNullException.ThrowIfNull(operation);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointAddress);
        var mediaType = operation.SoapVersion == SoapVersion.Soap12 ? "application/soap+xml" : "text/xml";
        request.Content = new StringContent(envelopeXml, Encoding.UTF8, mediaType);

        if (operation.SoapVersion == SoapVersion.Soap12)
        {
            request.Content.Headers.ContentType!.Parameters.Add(
                new NameValueHeaderValue("action", $"\"{operation.SoapAction}\""));
        }
        else
        {
            // SOAP 1.1 carries the action in a dedicated HTTP header.
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{operation.SoapAction}\"");
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        return new SoapInvocationResult(
            (int)response.StatusCode,
            response.ReasonPhrase,
            TryPrettyPrint(body),
            response.IsSuccessStatusCode,
            stopwatch.Elapsed);
    }

    private static string TryPrettyPrint(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return string.Empty;
        }

        try
        {
            return XDocument.Parse(xml).ToString();
        }
        catch (XmlException)
        {
            return xml;
        }
    }
}
