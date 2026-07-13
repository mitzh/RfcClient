# RfcClient

`RfcClient` 是一个面向依赖注入的 SAP RFC 客户端封装库，基于 SAP .NET Connector (NCo) 构建。

目标框架为 `.NET 10.0`。支持多 SAP RFC 连接配置、请求作用域内的 `ConfigId` 切换、强类型请求/响应映射，以及连接与调用监控扩展点。

更多文档：

- [项目分析与维护指南](docs/PROJECT_ANALYSIS.zh-CN.md)
- [文件调用依赖路线图](docs/file-dependency-roadmap.zh-CN.md)
- [English README](README.md)

## 功能特性

- 通过 `ConfigId` 管理多个 SAP RFC 连接配置
- 通过 `IsDefault` 选择默认配置
- 支持 ASP.NET Core / 通用主机的依赖注入
- 可通过 `IRfcClient.ConfigId` 在请求作用域内切换 RFC 会话
- 强类型 RFC 输入/输出映射（`[Table]` 和 `[Column]`）
- 连接与调用监控扩展点
- SAP NCo 运行时文件随项目 `libs` 目录提供

## 安装

安装 NuGet 包：

```bash
dotnet add package RfcClient --version 1.0.2
```

该包面向 `net10.0`，并且仅支持 Windows x64，因为随包提供的 SAP NCo 程序集是 AMD64 二进制文件。从 1.0.1 版本开始，当调用项目未显式指定目标平台或使用 `AnyCPU` 时，包会自动采用 `x64`；消费项目不需要添加 `Platforms` 或 `PlatformTarget` 属性。

## 配置

在 `appsettings.json` 中添加 RFC 连接配置：

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

`ConnectionString` 支持的参数映射到 `RfcConfigParameter`：

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

`ApplicationServer` / `Server` 用于直连应用服务器模式，`MessageServerHost` 用于消息服务器（负载均衡）模式。

## 注册服务

通过 `IServiceCollection` 注册客户端。可显式传入配置，也可让客户端自动从 DI 容器解析 `IConfiguration`：

```csharp
using mitzh;

// 方式一：手动传入配置
builder.Services.AddRfcClient(builder.Configuration);

// 方式二：自动从 DI 容器中解析 IConfiguration
builder.Services.AddRfcClient();
```

如果 SAP RFC 配置在某个配置节下：

```csharp
builder.Services.AddRfcClient(
    builder.Configuration.GetSection("Rfc"));
```

也支持编程方式注册：

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

项目公开命名空间为 `mitzh` 和 `mitzh.Abstractions`。

### Autofac Module 注册

`RfcClient` 同时支持构造函数注入和 Autofac 属性注入，推荐使用下面的构造函数注入方式。`RfcConfigProvider` 会直接从传入的 `IConfiguration` 绑定 `RfcOptions`；即使只向 `RfcClient` 注入 `IConfiguration`，它也会创建已绑定的默认配置提供器，不会创建空配置。

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

配置位于根节点时传入应用的 `IConfiguration`：

```csharp
builder.RegisterModule(new RfcModule(configuration));
```

配置位于 `Rfc` 子节点时传入该配置节：

```csharp
builder.RegisterModule(new RfcModule(configuration.GetSection("Rfc")));
```

如果使用 `AutofacServiceProviderFactory` 集成 ASP.NET Core，也可以继续通过 `builder.Services.AddRfcClient(...)` 注册，Microsoft DI 的 Options 注册会被 Autofac 容器接管。

## 定义 RFC 模型

在请求类型上使用 `[Table]` 声明 RFC 函数名。在需要映射的属性上使用 `[Column]` 指定 SAP RFC 参数字段名。

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

## 基本调用

注入 `IRfcClient`，使用强类型请求/响应模型调用 RFC：

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

`IRfcClient` 公开了一个作用域级别的 `ConfigId` 属性。若 `ConfigId` 为空，客户端将使用 `IsDefault=true` 的配置；若未标记任何默认配置，则使用 `RfcConnectionConfigs` 中的第一项。

当前调用方法为：

```csharp
TOut Invoke<TOut>(object input, string functionName = null, bool forceNew = false, string configId = null);
```

- `input` 为类时，显式传入的 `functionName` 优先于类上的 `[Table]` 特性。
- `input` 为字典时必须传入 `functionName`，字典键直接作为 RFC 参数名。
- `forceNew=true` 时绕过缓存的 Destination。
- 配置选择优先级为：方法参数 `configId` → 实例属性 `ConfigId` → 默认配置。

```csharp
var response = _rfcClient.Invoke<SupplyDemandResponse>(
    new Dictionary<string, object>
    {
        ["IV_MATNR"] = "B0505XT-1WR3",
        ["IV_BUKRS"] = "1100"
    },
    functionName: "ZFM_MM039");
```

## 按请求切换 ConfigId

在当前请求作用域内设置 `IRfcClient.ConfigId`，此后同一作用域内的所有 `IRfcClient` 调用都会自动使用该配置。

中间件示例：

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

控制器示例：

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

调用 API 时传入头部即可切换该请求的 SAP RFC 配置：

```http
X-Sap-Rfc-ConfigId: Sap.JSY
```

## 显式指定 ConfigId 调用

在需要仅为本次调用显式指定 RFC 配置时，直接传入 `configId`：

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

方法参数 `configId` 只影响本次调用，不会修改实例的 `ConfigId` 属性。

## 监控连接与调用

实现 `IRfcConnectionMonitor` 接口来观测已解析的 destination 和 RFC 调用：

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

在 `AddRfcClient` 前后注册监控器：

```csharp
using mitzh.Abstractions;

builder.Services.AddSingleton<IRfcConnectionMonitor, LoggingRfcConnectionMonitor>();
builder.Services.AddRfcClient(builder.Configuration);
```

请不要记录密码或完整连接字符串。

## 运行时文件

项目依赖的 SAP NCo 运行时文件位于 `libs` 目录：

```text
libs/cpc4n.dll
libs/ijwhost.dll
libs/sapnco.dll
libs/sapnco_utils.dll
```

从源码编译时，这些文件会被复制到输出根目录，与 `RfcClient.dll` 并列。在 NuGet 包中，托管 AMD64 程序集位于 `lib/net10.0/`，原生 `ijwhost.dll` 位于 `runtimes/win-x64/native/`。

包中还包含：

- `buildTransitive/RfcClient.props`：将未指定平台或使用 `AnyCPU` 的消费项目默认调整为 `x64`。
- `buildTransitive/RfcClient.targets`：在普通构建和发布时将 `ijwhost.dll` 复制到输出目录。

两个清理配置项位于 `RfcOptions` 中：

- `CleanupInterval`（默认 `00:05:00`）：客户端检查空闲 destination 的频率。
- `DestinationIdleTimeout`（默认 `00:10:00`）：destination 未被使用多久后从缓存中移除。

可在 `appsettings.json` 中或注册服务时通过代码设置。

## XML 文档

本库在编译时生成 XML 文档文件（`RfcClient.xml`），与程序集一同输出到构建目录。Visual Studio、JetBrains Rider 等 IDE 会自动加载它，为公共类型和成员提供中文内联说明。

## 构建与打包

```bash
dotnet build .\RfcClient.sln
dotnet pack .\RfcClient.csproj -c Release
```

生成的 NuGet 包位于：

```text
bin/Release/RfcClient.1.0.2.nupkg
```
