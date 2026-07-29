// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CoreWCF;

namespace CoreWcfSampleService;

[ServiceContract]
public interface IEchoService
{
    [OperationContract]
    string Echo(string text);
}

public sealed class EchoService : IEchoService
{
    public string Echo(string text) => $"You said: {text}";
}
