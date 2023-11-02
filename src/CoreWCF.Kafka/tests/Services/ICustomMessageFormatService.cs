// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ServiceModel;
using System.Text.Json.Serialization;
using System.Threading;

namespace Contracts;

[ServiceContract]
public interface ICustomMessageFormatService
{
    [OperationContract()]
    void OnPerson(Person person);
    // [OperationContract()]
    // void OnCity(City city);
}

public class CustomMessageFormatService : ICustomMessageFormatService
{
    public void OnPerson(Person person)
    {
        CountdownEvent.Signal(1);
        Person = person;
    }

    public void OnCity(City city)
    {
        CountdownEvent.Signal(1);
        City = city;
    }

    public CountdownEvent CountdownEvent { get; } = new(0);

    public Person Person { get; set; }
    public City City { get; set; }
}

public class Person
{
    [JsonPropertyName("lastName")]
    public string LastName { get; set; }
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }
}

public class City
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("country")]
    public string Country { get; set; }
}
