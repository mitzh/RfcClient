using mitzh.Abstractions;
using Microsoft.Extensions.Configuration;

namespace mitzh;

/// <summary>
///   SAP RFC 客户端的作用域实现。
///   在每个作用域内创建 RFC 会话来调用远程函数，并自动管理配置标识的解析。
///   此实现应通过依赖注入以作用域（Scoped）生命周期注册。
/// </summary>
public class RfcClient : IRfcClient
{
    private IRfcDestinationRegistry _destinationRegistry;
    private IRfcConfigProvider _configProvider;
    private IRfcConnectionMonitor _monitor;
    private IConfiguration _configuration;
    private bool _hasCustomConfigProvider;
    private bool _hasCustomDestinationRegistry;

    /// <summary>
    ///   初始化 <see cref="RfcClient"/> 类的新实例。
    ///   适用于属性注入；使用前必须设置 <see cref="Configuration"/> 或 <see cref="ConfigProvider"/>。
    /// </summary>
    public RfcClient()
    {
    }

    /// <summary>
    ///   使用应用程序配置初始化 <see cref="RfcClient"/> 类的新实例。
    ///   未单独注入配置提供程序时，将从该配置绑定 <see cref="RfcOptions"/>。
    /// </summary>
    /// <param name="configuration">
    ///   RFC 配置根节点；如果配置位于子节点，应传入对应的 <see cref="IConfigurationSection"/>。
    /// </param>
    public RfcClient(IConfiguration configuration)
    {
        Configuration = configuration;
    }

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
        _hasCustomConfigProvider = true;
        _hasCustomDestinationRegistry = true;
    }

    /// <summary>
    ///   获取或设置用于绑定 <see cref="RfcOptions"/> 的应用程序配置。
    ///   设置新配置后，未被显式替换的默认配置提供程序和目标注册表将重新创建。
    /// </summary>
    public virtual IConfiguration Configuration
    {
        get => _configuration;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _configuration = value;

            if (!_hasCustomConfigProvider)
            {
                _configProvider = null;
                ResetDefaultDestinationRegistry();
            }
        }
    }

    /// <summary>
    ///   获取或设置 RFC 连接监视器。未设置时使用 <see cref="RfcConnectionMonitor"/>。
    /// </summary>
    public virtual IRfcConnectionMonitor ConnectionMonitor
    {
        get => _monitor ??= new RfcConnectionMonitor();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _monitor = value;
            ResetDefaultDestinationRegistry();
        }
    }

    /// <summary>
    ///   获取或设置 RFC 配置提供程序。
    ///   未注入配置提供程序时会从 <see cref="Configuration"/> 创建默认实现。
    ///   两者都未设置时会引发异常，避免静默使用空的 RFC 配置。
    /// </summary>
    public virtual IRfcConfigProvider ConfigProvider
    {
        get => _configProvider ??= _configuration is null
            ? throw new InvalidOperationException(
                "IConfiguration or IRfcConfigProvider is required. Register RfcClient through AddRfcClient or provide an Autofac configuration registration.")
            : new RfcConfigProvider(_configuration);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _configProvider = value;
            _hasCustomConfigProvider = true;
            ResetDefaultDestinationRegistry();
        }
    }

    /// <summary>
    ///   获取或设置 RFC 目标注册表。未设置时使用 <see cref="RfcDestinationRegistry"/>。
    /// </summary>
    public virtual IRfcDestinationRegistry DestinationRegistry
    {
        get => _destinationRegistry ??= new RfcDestinationRegistry(ConfigProvider, ConnectionMonitor);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _destinationRegistry = value;
            _hasCustomDestinationRegistry = true;
        }
    }

    /// <summary>
    ///   获取或设置当前使用的 RFC 配置标识。
    ///   如果未设置，则使用配置提供程序返回的默认配置标识。
    ///   若设置的 ConfigId 未在 RfcConnectionConfigs 中注册，将自动回退到默认配置。
    /// </summary>
    public virtual string ConfigId { get; set; } = string.Empty;

    /// <summary>
    ///   调用远程 SAP RFC 函数，输入可为请求类或字典。
    /// </summary>
    /// <typeparam name="TOut">响应结果的类型，必须包含无参构造函数。</typeparam>
    /// <param name="input">请求类或字典。字典键作为 SAP RFC 参数名。</param>
    /// <param name="functionName">RFC 函数名。字典输入时必填；类输入时优先于 TableAttribute。</param>
    /// <param name="forceNew">是否强制使用新的连接目标（绕过缓存）。</param>
    /// <param name="configId">本次调用使用的配置标识。优先于实例的 <see cref="ConfigId"/>。</param>
    /// <returns>RFC 调用返回的结果对象。</returns>
    public virtual TOut Invoke<TOut>(
        object input,
        string functionName = null,
        bool forceNew = false,
        string configId = null)
        where TOut : new()
    {
        var effectiveConfigId = !string.IsNullOrWhiteSpace(configId)
            ? configId
            : !string.IsNullOrWhiteSpace(ConfigId)
                ? ConfigId
                : ConfigProvider.GetDefaultConfigId();

        if (!string.IsNullOrWhiteSpace(effectiveConfigId)
            && !RfcConnectionManager.IsDestinationRegistered(effectiveConfigId))
        {
            effectiveConfigId = ConfigProvider.GetDefaultConfigId();
        }

        using var session = new RfcSession(effectiveConfigId, DestinationRegistry, ConnectionMonitor);
        return session.Invoke<TOut>(input, functionName, forceNew);
    }

    private void ResetDefaultDestinationRegistry()
    {
        if (!_hasCustomDestinationRegistry)
        {
            _destinationRegistry = null;
        }
    }
}
