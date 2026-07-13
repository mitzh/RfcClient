# RfcClient

`RfcClient` is a DI-friendly SAP RFC client wrapper for SAP .NET Connector.

It targets `.NET 10.0`. The library supports multiple SAP RFC connection configs, request-scoped `ConfigId` switching, typed request/response mapping, and connection/invocation monitoring hooks.

Additional documentation:

- [Project analysis and maintenance guide](docs/PROJECT_ANALYSIS.en-US.md)
- [File dependency roadmap](docs/file-dependency-roadmap.en-US.md)
- [中文项目分析与维护指南](docs/PROJECT_ANALYSIS.zh-CN.md)
- [中文版 README](README.zh-CN.md)

## Features

- Multiple SAP RFC connection configs by `ConfigId`
- Default config selection through `IsDefault`
- ASP.NET Core / generic host dependency injection support
- Request-scoped RFC session switching through `IRfcClient.ConfigId`
- Typed RFC input/output mapping with `[Table]` and `[Column]`
- Connection and invocation monitoring extension points
- SAP NCo runtime files included from project `libs`

## Installation

Install the NuGet package:

```bash
dotnet add package RfcClient --version 1.0.2
```

The package targets `net10.0` and requires Windows x64 because the bundled SAP NCo assemblies are AMD64 binaries. Starting with version 1.0.1, the package automatically changes an unspecified or `AnyCPU` consumer target to `x64`; consuming projects do not need to add `Platforms` or `PlatformTarget` properties.

## Configuration

Add RFC connection configs to `appsettings.json`:

```json
{
  "RfcConnectionConfigs": [
    {
      "ConfigId": "Sap",
      "IsDefault": true,
      "ConnectionString": "ApplicationServer=192.168.1.65;SystemNumber=00;SystemId=DEV;Client=800;UserName=DEVUSER;Password=******;Language=ZH;PoolSize=5;MaxPoolSize=10;ConnectionTimeout=30;CommunicationTimeout=60;"
    },
    {
      "ConfigId": "Sap.JSY",
      "IsDefault": false,
      "ConnectionString": "MessageServerHost=192.168.1.65;MessageServerService=3600;SystemId=DEV;Client=800;UserName=DEVUSER;Password=******;Language=ZH;PoolSize=5;MaxPoolSize=10;"
    }
  ]
}
```

Supported `ConnectionString` parameters map to `RfcConfigParameter`:

```text
ApplicationServer
Server
SystemNumber
SystemId
Client
UserName
User ID
UserId
Password
Language
PoolSize
MaxPoolSize
Max Pool Size
ConnectionTimeout
CommunicationTimeout
MessageServerHost
MessageServerService
MessageServerPort
```

`ApplicationServer` / `Server` is used for direct application server connections. `MessageServerHost` is used for message server connections.

## Register Services

Register the client with `IServiceCollection`. Pass the configuration explicitly, or let it auto-resolve `IConfiguration` from the DI container:

```csharp
using mitzh;

// Option 1: pass configuration explicitly
builder.Services.AddRfcClient(builder.Configuration);

// Option 2: auto-resolve IConfiguration from the DI container
builder.Services.AddRfcClient();
```

If your SAP RFC settings live under a section:

```csharp
builder.Services.AddRfcClient(
    builder.Configuration.GetSection("Rfc"));
```

Programmatic registration is also supported:

```csharp
using mitzh;

builder.Services.AddRfcClient(options =>
{
    options.RfcConnectionConfigs.Add(new RfcConnectionConfig
    {
        ConfigId = "Sap",
        IsDefault = true,
        ConnectionString = "ApplicationServer=192.168.1.65;SystemNumber=00;Client=800;UserName=DEVUSER;Password=******;"
    });
});
```

The public namespaces are `mitzh` and `mitzh.Abstractions`.

### Autofac Module registration

`RfcClient` supports constructor injection and Autofac property injection; the constructor-injection setup below is recommended. `RfcConfigProvider` binds `RfcOptions` directly from the supplied `IConfiguration`. Even when only `IConfiguration` is injected into `RfcClient`, it creates a bound default provider instead of an empty fallback configuration.

```csharp
using Autofac;
using Microsoft.Extensions.Configuration;
using mitzh;
using mitzh.Abstractions;

public sealed class RfcModule : Module
{
    private readonly IConfiguration _configuration;

    public RfcModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<RfcConnectionMonitor>()
            .As<IRfcConnectionMonitor>()
            .SingleInstance();

        builder.Register(_ => new RfcConfigProvider(_configuration))
            .As<IRfcConfigProvider>()
            .SingleInstance();

        builder.RegisterType<RfcDestinationRegistry>()
            .As<IRfcDestinationRegistry>()
            .SingleInstance();

        builder.RegisterType<RfcClient>()
            .As<IRfcClient>()
            .InstancePerLifetimeScope();
    }
}
```

Pass the application configuration when RFC settings are at the root:

```csharp
builder.RegisterModule(new RfcModule(configuration));
```

Pass the corresponding section when RFC settings live under `Rfc`:

```csharp
builder.RegisterModule(new RfcModule(configuration.GetSection("Rfc")));
```

When ASP.NET Core uses `AutofacServiceProviderFactory`, `builder.Services.AddRfcClient(...)` remains supported because Autofac takes over the Microsoft DI Options registrations.

## Define RFC Models

Use `[Table]` on the request type to declare the RFC function name. Use `[Column]` on properties to map SAP RFC parameter names.

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("ZFM_MM039")]
public class SupplyDemandRequest
{
    [Required]
    [Column("IV_MATNR")]
    public string MaterialCode { get; set; } = string.Empty;

    [Required]
    [Column("IV_BUKRS")]
    public string CompanyCode { get; set; } = string.Empty;

    [Column("IV_WERKS")]
    public string PlantCode { get; set; } = string.Empty;

    [Required]
    [Column("IV_SYSTEM")]
    public string Source { get; set; } = string.Empty;
}
```

```csharp
using System.ComponentModel.DataAnnotations.Schema;

public class SupplyDemandResponse
{
    [Column("EV_STATUS")]
    public string Status { get; set; } = string.Empty;

    [Column("EV_MESSAGE")]
    public string Message { get; set; } = string.Empty;

    [Column("ET_DATA")]
    public SupplyDemandRow[] Rows { get; set; } = Array.Empty<SupplyDemandRow>();
}

public class SupplyDemandRow
{
    [Column("MATNR")]
    public string MaterialCode { get; set; } = string.Empty;

    [Column("WERKS")]
    public string PlantCode { get; set; } = string.Empty;

    [Column("BDMNG")]
    public decimal Quantity { get; set; }
}
```

## Basic Call

Inject `IRfcClient` and invoke the RFC with typed request/response models:

```csharp
using mitzh.Abstractions;

public class SupplyDemandService
{
    private readonly IRfcClient _rfcClient;

    public SupplyDemandService(IRfcClient rfcClient)
    {
        _rfcClient = rfcClient;
    }

    public SupplyDemandResponse Query()
    {
        var request = new SupplyDemandRequest
        {
            MaterialCode = "B0505XT-1WR3",
            CompanyCode = "1100",
            PlantCode = "",
            Source = "C"
        };

        return _rfcClient.Invoke<SupplyDemandResponse>(request);
    }
}
```

`IRfcClient` exposes a scoped `ConfigId` property. If `ConfigId` is empty, the client uses the config marked with `IsDefault=true`. If no config is marked as default, it uses the first item in `RfcConnectionConfigs`.

The current invocation API is:

```csharp
TOut Invoke<TOut>(object input, string functionName = null, bool forceNew = false, string configId = null);
```

- For a class input, an explicit `functionName` takes precedence over its `[Table]` attribute.
- For a dictionary input, `functionName` is required and dictionary keys are used as RFC parameter names.
- Set `forceNew` to `true` to bypass the cached destination.
- Config selection priority is: method `configId` → instance `ConfigId` → default config.

```csharp
var response = _rfcClient.Invoke<SupplyDemandResponse>(
    new Dictionary<string, object>
    {
        ["IV_MATNR"] = "B0505XT-1WR3",
        ["IV_BUKRS"] = "1100"
    },
    functionName: "ZFM_MM039");
```

## Switch ConfigId Per Request

Set `IRfcClient.ConfigId` inside the current request scope. After that, all `IRfcClient` calls in the same scope use that config automatically.

Example middleware:

```csharp
using mitzh.Abstractions;

app.Use(async (context, next) =>
{
    var rfcClient = context.RequestServices.GetRequiredService<IRfcClient>();
    var configId = context.Request.Headers["X-Sap-Rfc-ConfigId"].FirstOrDefault();

    if (!string.IsNullOrWhiteSpace(configId))
    {
        rfcClient.ConfigId = configId;
    }

    await next();
});
```

Example controller:

```csharp
using Microsoft.AspNetCore.Mvc;
using mitzh.Abstractions;

[ApiController]
[Route("api/supply-demand")]
public class SupplyDemandController : ControllerBase
{
    private readonly IRfcClient _rfcClient;

    public SupplyDemandController(IRfcClient rfcClient)
    {
        _rfcClient = rfcClient;
    }

    [HttpPost]
    public ActionResult<SupplyDemandResponse> Query(SupplyDemandRequest request)
    {
        var response = _rfcClient.Invoke<SupplyDemandResponse>(request);
        return Ok(response);
    }
}
```

Calling the API with a header switches the SAP RFC config for that request:

```http
X-Sap-Rfc-ConfigId: Sap.JSY
```

## Call With Explicit ConfigId

Pass `configId` when a config should apply only to the current invocation:

```csharp
using mitzh.Abstractions;

public class ManualRfcService
{
    private readonly IRfcClient _rfcClient;

    public ManualRfcService(IRfcClient rfcClient)
    {
        _rfcClient = rfcClient;
    }

    public SupplyDemandResponse QueryWithJsy(SupplyDemandRequest request)
    {
        return _rfcClient.Invoke<SupplyDemandResponse>(request, configId: "Sap.JSY");
    }
}
```

The method-level `configId` affects only the current invocation and does not change the instance `ConfigId` property.

## Monitor Connections And Calls

Implement `IRfcConnectionMonitor` to observe resolved destinations and RFC invocations:

```csharp
using Microsoft.Extensions.Logging;
using mitzh;
using mitzh.Abstractions;

public class LoggingRfcConnectionMonitor : IRfcConnectionMonitor
{
    private readonly ILogger<LoggingRfcConnectionMonitor> _logger;

    public LoggingRfcConnectionMonitor(ILogger<LoggingRfcConnectionMonitor> logger)
    {
        _logger = logger;
    }

    public void DestinationResolved(RfcDestinationResolvedContext context)
    {
        _logger.LogInformation(
            "SAP RFC destination resolved. ConfigId={ConfigId}, ForceNew={ForceNew}, PoolSize={PoolSize}, MaxPoolSize={MaxPoolSize}",
            context.ConfigId,
            context.ForceNew,
            context.ConfigParameter.PoolSize,
            context.ConfigParameter.MaxPoolSize);
    }

    public void InvocationStarted(RfcInvocationContext context)
    {
        _logger.LogInformation(
            "SAP RFC invocation started. ConfigId={ConfigId}, Function={Function}",
            context.ConfigId,
            context.FunctionName);
    }

    public void InvocationSucceeded(RfcInvocationContext context)
    {
        _logger.LogInformation(
            "SAP RFC invocation succeeded. ConfigId={ConfigId}, Function={Function}, Elapsed={ElapsedMs}ms",
            context.ConfigId,
            context.FunctionName,
            context.Elapsed.TotalMilliseconds);
    }

    public void InvocationFailed(RfcInvocationContext context, Exception exception)
    {
        _logger.LogError(
            exception,
            "SAP RFC invocation failed. ConfigId={ConfigId}, Function={Function}, Elapsed={ElapsedMs}ms",
            context.ConfigId,
            context.FunctionName,
            context.Elapsed.TotalMilliseconds);
    }
}
```

Register the monitor before or after `AddRfcClient`:

```csharp
using mitzh.Abstractions;

builder.Services.AddSingleton<IRfcConnectionMonitor, LoggingRfcConnectionMonitor>();
builder.Services.AddRfcClient(builder.Configuration);
```

Do not log passwords or full connection strings.

## Runtime Files

The project expects SAP NCo runtime files under the `libs` folder:

```text
libs/cpc4n.dll
libs/ijwhost.dll
libs/sapnco.dll
libs/sapnco_utils.dll
```

During a source build, these files are copied to the output root directory alongside `RfcClient.dll`. In the NuGet package, managed AMD64 assemblies are stored under `lib/net10.0/`, while `ijwhost.dll` is stored under `runtimes/win-x64/native/`.

The package also contains:

- `buildTransitive/RfcClient.props`: defaults an unspecified or `AnyCPU` consumer to `x64`.
- `buildTransitive/RfcClient.targets`: copies `ijwhost.dll` to normal build and publish output directories.

Configuration options: the library exposes two timing options in `RfcOptions`:

- `CleanupInterval` (default `00:05:00`): how often the client checks for idle destinations to cleanup.
- `DestinationIdleTimeout` (default `00:10:00`): how long a destination can stay unused before being removed from the cache.

You can set these in `appsettings.json` or via code when registering the services.

## XML Documentation

The library generates an XML documentation file (`RfcClient.xml`) alongside the assembly in the build output. IDEs such as Visual Studio and JetBrains Rider automatically pick it up to provide inline Chinese descriptions for public types and members.

## Build And Pack

```bash
dotnet build .\RfcClient.sln
dotnet pack .\RfcClient.csproj -c Release
```

The package is generated under:

```text
bin/Release/RfcClient.1.0.2.nupkg
```
