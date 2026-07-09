using RfcClient.Abstractions;

namespace RfcClient;

/// <summary>
///   SAP RFC 客户端的作用域实现。
///   在每个作用域内创建 RFC 会话来调用远程函数，并自动管理配置标识的解析。
///   此实现应通过依赖注入以作用域（Scoped）生命周期注册。
/// </summary>
public class RfcClient : IRfcClient
{
    private readonly IRfcDestinationRegistry _destinationRegistry;
    private readonly IRfcConfigProvider _configProvider;
    private readonly IRfcConnectionMonitor _monitor;

    /// <summary>
    ///   初始化 <see cref="RfcClient"/> 类的新实例。
    /// </summary>
    /// <param name="destinationRegistry">RFC 目标注册表。</param>
    /// <param name="configProvider">RFC 配置提供程序。</param>
    /// <param name="monitor">RFC 连接监视器。</param>
    public RfcClient(
        IRfcDestinationRegistry destinationRegistry,
        IRfcConfigProvider configProvider,
        IRfcConnectionMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(destinationRegistry);
        ArgumentNullException.ThrowIfNull(configProvider);
        ArgumentNullException.ThrowIfNull(monitor);

        _destinationRegistry = destinationRegistry;
        _configProvider = configProvider;
        _monitor = monitor;
    }

    /// <summary>
    ///   获取或设置当前使用的 RFC 配置标识。
    ///   如果未设置，则使用配置提供程序返回的默认配置标识。
    ///   若设置的 ConfigId 未在 RfcConnectionConfigs 中注册，将自动回退到默认配置。
    /// </summary>
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>
    ///   调用远程 SAP RFC 函数并获取返回值。
    ///   如果未设置 ConfigId，将自动使用默认配置标识。
    ///   如果 ConfigId 未找到对应的注册目标，也会回退到默认配置。
    /// </summary>
    /// <typeparam name="TIn">请求参数的类型，必须为类类型。</typeparam>
    /// <typeparam name="TOut">响应结果的类型，必须包含无参构造函数。</typeparam>
    /// <param name="input">请求参数对象。</param>
    /// <param name="forceNew">是否强制使用新的连接目标（绕过缓存）。默认值为 false。</param>
    /// <returns>RFC 调用返回的结果对象。</returns>
    public TOut Invoke<TIn, TOut>(TIn input, bool forceNew = false)
        where TIn : class
        where TOut : new()
    {
        var configId = string.IsNullOrWhiteSpace(ConfigId)
            ? _configProvider.GetDefaultConfigId()
            : ConfigId;

        // 如果指定的 ConfigId 未注册，回退到默认配置
        if (!string.IsNullOrWhiteSpace(configId) && !RfcConnectionManager.IsDestinationRegistered(configId))
        {
            configId = _configProvider.GetDefaultConfigId();
        }

        using var session = new RfcSession(configId, _destinationRegistry, _monitor);
        return session.Invoke<TIn, TOut>(input, forceNew);
    }
}
