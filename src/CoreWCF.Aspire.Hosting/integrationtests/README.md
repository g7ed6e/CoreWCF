# Hosting integration tests

End-to-end tests for `CoreWCF.Aspire.Hosting`. They start a real Aspire AppHost, which runs the real
explorer **container image** and a real CoreWCF service, and then drive the explorer over HTTP.

| Project | What it is |
| --- | --- |
| `CoreWcfExplorer.IntegrationTests.AppHost` | The AppHost under test. Registers the explorer with `AddCoreWcfExplorer` — the container path the package ships — rather than the `AddProject` shortcut the sample uses. |
| `CoreWCF.Aspire.Hosting.IntegrationTests` | The tests, driven through `Aspire.Hosting.Testing`. |

Both the explorer **and** the CoreWCF service run as containers. That is forced by the Aspire version
this AppHost is pinned to: on the 9.5.2 support floor a container cannot reach a proxied *project*
endpoint on Linux. The address handed to the container is `host.docker.internal`, which does not
resolve there, and mapping it onto the bridge gateway only moves the failure along to
`Connection refused`, because the proxy is not listening on that interface. Aspire 13.3 fixed this
properly with the container tunnel; below it, putting both resources on the container network — where
they address each other by resource name — is the only way to exercise the explorer against a real
service.

## Why these are separate from the unit tests

`CoreWCF.Aspire.Hosting.Tests` next door asserts on the *resource model* — image coordinates, endpoint,
the `CoreWcf__Services__N__*` environment projection. All of that can be correct while the integration
is still broken, because none of it establishes that:

- the image exists and boots at all;
- the `/health` probe `AddCoreWcfExplorer` attaches actually answers;
- the endpoint addresses `WithCoreWcfService` projects are reachable **from inside a container** —
  the service runs as a host process, the explorer does not;
- the explorer serves its static web assets outside Development, which is the only environment the
  container ever runs in.

Only a real container run shows those, and each has been a live bug at some point.

## Running them

They need a **Linux container runtime** and are Linux-container only. Build both images, tag the
explorer under the registry the package hardcodes, then point the tests at the tag:

```bash
dotnet publish src/CoreWCF.Aspire.Explorer/src/CoreWCF.Aspire.Explorer.csproj \
  -c Release -t:PublishContainer -p:ContainerImageTag=local-test
docker tag corewcf/aspire-explorer:local-test ghcr.io/corewcf/aspire-explorer:local-test

dotnet publish src/CoreWCF.Aspire.Hosting/samples/CoreWcfSampleService/CoreWcfSampleService.csproj \
  -c Release -t:PublishContainer -p:ContainerImageTag=local-test

CoreWcfExplorer__ImageTag=local-test CoreWcfSampleService__ImageTag=local-test \
  dotnet test src/CoreWCF.Aspire.Hosting/integrationtests/CoreWCF.Aspire.Hosting.IntegrationTests
```

Aspire uses a locally tagged image as-is; nothing is pulled. With `CoreWcfExplorer__ImageTag` unset
the AppHost falls back to the package default (`latest`), which is what a consumer of the published
package gets — so an unset variable tests the published image, not a local build.

To confirm the topology rather than just the result, inspect the explorer container while the tests
run: `CoreWcf__Services__0__Url` should read `http://echo-service:8080`. A `host.docker.internal`
address there means the service is being run as a project again, and the tests will pass on Docker
Desktop while failing on Linux.

## Why they are not in the CI test matrix

`…IntegrationTests.csproj` deliberately does not match `CoreWCF.*.Tests.csproj`, the glob
`.github/actions/run-tests` sweeps. That matrix runs on Windows and Linux across five target
frameworks and has no container runtime; these tests need one, and need it in Linux mode. They run
in their own ubuntu-only job instead.

Renaming either project so that it ends in `.Tests` would silently enrol it in every CI leg.
