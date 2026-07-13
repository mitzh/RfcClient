using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using mitzh.Abstractions;

namespace mitzh;

/// <summary>
///   RFC 配置提供程序的默认实现。
///   从 <see cref="RfcOptions"/> 中读取配置，并向连接管理器注册清理参数。
/// </summary>
public class RfcConfigProvider : IRfcConfigProvider
{
    private readonly RfcOptions _options;

    /// <summary>
    ///   使用应用程序配置初始化 <see cref="RfcConfigProvider"/> 类的新实例。
    ///   适用于 Autofac 等直接注入 <see cref="IConfiguration"/> 的容器。
    /// </summary>
    /// <param name="configuration">
    ///   RFC 配置根节点；如果配置位于子节点，应传入对应的 <see cref="IConfigurationSection"/>。
    /// </param>
    public RfcConfigProvider(IConfiguration configuration)
        : this(CreateOptions(configuration))
    {
    }

    /// <summary>
    ///   初始化 <see cref="RfcConfigProvider"/> 类的新实例。
    /// </summary>
    /// <param name="options">包含 RFC 连接配置的选项对象。</param>
    public RfcConfigProvider(IOptions<RfcOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        // Apply cleanup configuration for connection manager from options
        RfcConnectionManager.ConfigureCleanup(_options.CleanupInterval, _options.DestinationIdleTimeout);
    }

    /// <summary>
    ///   获取默认的 RFC 配置标识。
    /// </summary>
    /// <returns>默认配置标识字符串。</returns>
    public string GetDefaultConfigId()
    {
        return _options.GetConfigId();
    }

    /// <summary>
    ///   根据配置标识获取对应的 RFC 连接参数。
    /// </summary>
    /// <param name="configId">RFC 配置标识。</param>
    /// <returns>对应配置标识的 RfcConfigParameter 对象。</returns>
    public RfcConfigParameter GetConfigParameter(string configId)
    {
        return _options.GetRfcOptions(configId);
    }

    /// <summary>
    ///   获取所有可用的 RFC 配置参数的只读字典。
    /// </summary>
    /// <returns>包含所有配置信息的只读字典。</returns>
    public IReadOnlyDictionary<string, RfcConfigParameter> GetConfigParameters()
    {
        return _options.GetRfcDestinations();
    }

    /// <summary>
    ///   将应用程序配置绑定为 RFC 选项。
    /// </summary>
    /// <param name="configuration">RFC 配置根节点。</param>
    /// <returns>包含已绑定 RFC 配置的选项包装器。</returns>
    private static IOptions<RfcOptions> CreateOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RfcOptions();
        configuration.Bind(options);
        return Options.Create(options);
    }
}
