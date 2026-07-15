# RfcClient 项目分析与维护指南

## 项目概览

`RfcClient` 1.0.5 是一个面向 .NET 10 和 Windows x64 的类库，用于在 SAP .NET Connector (NCo) 之上提供依赖注入、命名 RFC 配置、强类型请求/响应映射、作用域级配置切换，以及连接和调用监控钩子。

公开实现类型位于 `Mitzh` 命名空间，抽象接口位于 `Mitzh.Abstractions`。`RfcClient` 同时支持 Microsoft DI 和 Autofac 构造注入，也保留属性注入入口；当前调用入口为 `Invoke<TOut>(object input, string functionName = null, bool forceNew = false, string configId = null)`。

项目要求 SAP NCo 运行时文件位于 `libs/` 目录：

- `cpc4n.dll`
- `ijwhost.dll`
- `sapnco.dll`
- `sapnco_utils.dll`

`cpc4n.dll`、`sapnco.dll` 和 `sapnco_utils.dll` 都是 AMD64 托管程序集，因此项目和运行进程必须使用 x64。NuGet 包通过 `buildTransitive/RfcClient.props` 自动将未指定平台或使用 `AnyCPU` 的消费项目调整为 `x64`，无需消费项目手工添加 `Platforms` 或 `PlatformTarget`。

包内运行时布局如下：

- `lib/net10.0/`：`RfcClient.dll`、XML 文档及三个 AMD64 托管程序集。
- `runtimes/win-x64/native/ijwhost.dll`：C++/CLI 所需的原生运行库。
- `buildTransitive/RfcClient.targets`：将 `ijwhost.dll` 复制到消费项目的构建和发布目录。

## 架构说明

主要调用链如下：

```text
IRfcClient
  -> RfcClient
  -> RfcSession
  -> IRfcDestinationRegistry
  -> RfcConnectionManager
  -> SAP.Middleware.Connector.RfcDestination
```

核心组件职责：

- `RfcServiceCollectionExtensions`：注册依赖注入服务。
- `IRfcClient`：暴露 `ConfigId` 和强类型 RFC 调用。
- `RfcClient`：解析实际使用的 `ConfigId`，创建短生命周期 `RfcSession`，并委托执行调用。
- `RfcOptions`：校验配置并将连接字符串转换为 RFC 参数。
- `RfcConfigProvider`：可从 `IOptions<RfcOptions>` 或 `IConfiguration` 绑定 RFC 连接配置，并应用连接清理参数。
- `RfcDestinationRegistry`：注册和解析命名 SAP destination。
- `RfcConnectionManager`：管理 SAP NCo destination 配置、destination 缓存和空闲清理。
- `RfcSession`：执行强类型 RFC 调用，并发送监控事件。
- `RfcTypeConverter`：根据 `[Column]` 映射 SAP RFC 标量、结构和表数据。
- `RfcRequestMetadata`：集中处理请求模型校验和 RFC 函数名解析。

## 使用摘要

注册服务：

```csharp
builder.Services.AddRfcClient(builder.Configuration);
```

配置命名连接：

```json
{
  "RfcConnectionConfigs": [
    {
      "ConfigId": "Sap",
      "IsDefault": true,
      "ConnectionString": "ApplicationServer=192.168.1.65;SystemNumber=00;Client=800;UserName=DEVUSER;Password=******;Language=ZH;"
    }
  ]
}
```

调用 RFC：

```csharp
public sealed class SupplyDemandService
{
    private readonly IRfcClient _rfcClient;

    public SupplyDemandService(IRfcClient rfcClient)
    {
        _rfcClient = rfcClient;
    }

    public SupplyDemandResponse Query(SupplyDemandRequest request)
    {
        return _rfcClient.Invoke<SupplyDemandResponse>(request);
    }
}
```

在当前 scope 内切换 SAP 配置：

```csharp
_rfcClient.ConfigId = "Sap.JSY";
var response = _rfcClient.Invoke<SupplyDemandResponse>(request);
```

请求类型必须使用 `[Table("RFC_FUNCTION_NAME")]` 标记 RFC 函数名；请求和响应模型中需要映射的属性应使用 `[Column("SAP_FIELD_NAME")]`。

## 本次重构内容

1.0.1 平台与打包调整：

- 将项目和解决方案平台统一为 `x64`。
- 增加传递构建配置，使消费项目无需显式声明 `Platforms` 和 `PlatformTarget`。
- 将原生 `ijwhost.dll` 从 `lib/net10.0/` 移至 `runtimes/win-x64/native/`，避免 NuGet 将其误当作托管程序集解析。
- 增加传递复制目标，确保普通构建和发布目录都包含 `ijwhost.dll`。
- 将根命名空间统一为 `Mitzh`，抽象接口统一为 `Mitzh.Abstractions`。

1.0.2 Autofac 配置绑定修复：

- `RfcConfigProvider` 新增 `IConfiguration` 构造路径，支持根配置和指定配置节。
- Microsoft DI 使用显式工厂选择 `IOptions<RfcOptions>` 构造路径，避免双构造函数歧义。
- `RfcClient` 可从注入的 `IConfiguration` 创建已绑定的默认配置提供器，不再回退到空 `RfcOptions`；配置与提供器都缺失时立即抛出明确异常。
- Autofac Module 通过 `new RfcConfigProvider(configuration)` 显式绑定配置，再以构造函数方式注入 `RfcClient`。

1.0.3 输入映射调整：

- `RfcTypeConverter.SetInputValue<T>` 遇到 `[Column]` 名称为 `null`、空字符串或纯空白的属性时直接跳过，不再在 RFC 调用前失败。
- 输出映射继续严格校验列名，使错误的响应模型仍能获得明确异常。

1.0.5 RFC TABLE 输入映射：

- 当目标 RFC 参数为 TABLE 时，将数组和 `List<T>`/`IList` 集合转换为 `IRfcTable` 行。
- 行对象使用 `[Column]` 映射，字典行使用字符串键作为 SAP 字段名。
- `byte[]` 继续作为普通 RFC 值处理，不会被误判为表集合。

本次维护调整了作用域级配置切换 API：

- 在 `IRfcClient` 上新增 `ConfigId`。
- 移除之前面向 DI 的配置访问器和会话工厂服务。
- 将实际 `ConfigId` 解析与 session 创建移动到 `RfcClient`。
- 保留 `RfcSession` 作为每次调用的内部执行对象。

项目中已有的维护内容还包括：

- 新增 `RfcRequestMetadata`，集中处理请求模型校验和 RFC 函数名解析。
- `RfcSession` 复用集中化的请求元数据逻辑。
- 修复 `AddRfcClient(RfcOptions)` 未复制连接清理参数的问题。
- 在 `RfcOptions` 中增加空 `ConfigId`、重复 `ConfigId` 和无效默认 `ConfigId` 校验。
- 在 `RfcConnectionManager` 中增加清理间隔参数校验。
- 简化空闲 destination 清理逻辑和 destination 缓存创建逻辑。
- 合并 `RfcTypeConverter` 中参数与字段转换的重复 switch 逻辑。
- 更新 README 中目标框架和运行时文件打包路径说明。

## 构建与验证

构建：

```bash
dotnet build .\RfcClient.sln -c Release
```

打包：

```bash
dotnet pack .\RfcClient.csproj -c Release -p:Platform=x64 -o .\bin\Release
```

输出包为 `bin/Release/RfcClient.1.0.5.nupkg`。发布由 `.github/workflows/publish-nuget.yml` 完成；推送 `v*` 版本标签或手动触发工作流都会使用 NuGet Trusted Publishing 上传包。

## 维护建议

- 保持目标框架、Microsoft.Extensions 依赖版本和 README 中的框架说明一致。
- 连接字符串和密码属于敏感信息，不要记录完整连接字符串。
- 扩展映射能力前，优先为 `RfcOptions`、`RfcClient` 和 `RfcTypeConverter` 增加单元测试。
- 如果需要支持旧版本业务系统，应评估多目标框架，而不是只修改 README 文档。
- SAP NCo 文件仅支持 Windows x64；发布前应使用一个没有显式平台设置的空白消费项目验证自动架构选择、构建输出和发布输出。
