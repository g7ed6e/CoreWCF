# CoreWCF.Extensions.Configuration

Declares CoreWCF bindings, services and endpoints in `IConfiguration`, rather than in the
`<system.serviceModel>` XML that `CoreWCF.ConfigurationManager` reads from a `wcf.config` file.

```jsonc
"ServiceModel": {
  "Bindings": {
    "internal": {
      "Type": "CoreWCF.NetTcpBinding, CoreWCF.NetTcp",
      "MaxReceivedMessageSize": 2097152,
      "Security": { "Mode": "Transport" }
    }
  },
  "Services": {
    "Contoso.EchoService": {
      "Endpoints": [
        {
          "Contract": "Contoso.IEchoService",
          "Binding": "internal",
          "Address": "net.tcp://localhost:8089/echo"
        }
      ]
    }
  }
}
```

```csharp
services.AddServiceModelServices();
services.AddServiceModelConfiguration(configuration.GetSection("ServiceModel"));
```

An endpoint's `Binding` is either the name of an entry under `Bindings`, or an inline binding object when the
binding is used once.

## Naming a binding type

**The `Type` discriminator is an assembly qualified name.** Short names such as `"NetTcpBinding"` are rejected.

This is deliberate, and the reason is in this repository rather than in theory. CoreWCF ships client and server
halves of the queue transports side by side, and they are deliberate homonyms:

| | Type | Assembly |
|---|---|---|
| server | `CoreWCF.Channels.KafkaBinding` | `CoreWCF.Kafka` |
| client | `CoreWCF.ServiceModel.Channels.KafkaBinding` | `CoreWCF.Kafka.Client` |

`RabbitMqBinding` and `RabbitMqTransportBindingElement` are the same story. A registry keyed by short name resolves
`"KafkaBinding"` to whichever assembly it happened to scan last — a silent, ordering-dependent wrong answer. The
namespaces already tell the two apart, because client types live under `CoreWCF.ServiceModel.*` mirroring
`System.ServiceModel.*`, so the full name is the smallest key that cannot collide. Across the 35 binding types in
this repository no full name is duplicated, while three short names are.

Naming the assembly as well is what makes resolution *deterministic*. Transports load lazily, so resolving a name
by searching the assemblies already loaded gives an answer that depends on what the application happened to touch
first — it works on the machine where it was written and fails elsewhere. An assembly qualified name loads its
assembly rather than waiting for something else to.

This also keeps the package honest about its dependencies: it references `CoreWCF.Primitives` and no transport.
Eleven CoreWCF assemblies declare binding types, from HTTP and net.tcp through to MSMQ, Kafka and RabbitMQ, and
referencing them to populate a registry would drag every transport into an application that wanted one. The
application brings the transports it uses; configuration names them.

The cost is verbosity, and a host that would rather not repeat an assembly qualified name can register its own:

```csharp
services.AddSingleton(new BindingHydrator(new BindingHydratorOptions
{
    Registry = new BindingTypeRegistry()
        .Add("netTcp", typeof(CoreWCF.NetTcpBinding))
        .Add("basicHttp", typeof(CoreWCF.BasicHttpBinding)),
}));
```

Registering two types under one name is an error rather than a silent overwrite.

Service and contract type names are resolved differently: a plain full name is looked up across the loaded
assemblies, because they are the application's own types and its assembly is necessarily loaded. This asymmetry
with binding names is deliberate but worth revisiting if it proves confusing.

## Custom bindings

`CustomBinding` takes an ordered list of binding elements, each naming its own type:

```jsonc
"soap12": {
  "Type": "CoreWCF.Channels.CustomBinding, CoreWCF.Primitives",
  "Elements": [
    {
      "Type": "CoreWCF.Channels.TextMessageEncodingBindingElement, CoreWCF.Primitives",
      "MessageVersion": "Soap12WSAddressing10",
      "WriteEncoding": "utf-8"
    },
    {
      "Type": "CoreWCF.Channels.HttpTransportBindingElement, CoreWCF.Http",
      "MaxReceivedMessageSize": 1048576
    }
  ]
}
```

Values such as `MessageVersion`, `EnvelopeVersion`, `SecurityAlgorithmSuite` and `MessageSecurityVersion` have no
`TypeConverter`, so they are resolved by looking up a public static member of the target type by name —
`MessageVersion.Soap12WSAddressing10` here. One lookup covers the whole family and keeps working for types added
later.

## Unknown keys are errors

A key that does not match a property fails, naming its configuration path, rather than being silently ignored.
