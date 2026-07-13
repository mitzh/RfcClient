using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using mitzh.Abstractions;

namespace mitzh;

/// <summary>
///   RFC 客户端服务注册扩展方法。
///   提供 IServiceCollection 的扩展方法，用于向依赖注入容器注册 SAP RFC 客户端相关服务。
/// </summary>
public static class RfcServiceCollectionExtensions
{
    /// <summary>
    ///   注册 SAP RFC 客户端服务。
    ///   自动从依赖注入容器中解析 IConfiguration 并绑定到 <see cref="RfcOptions"/>。
    ///   适用于 ASP.NET Core 等已注册 IConfiguration 的宿主环境。
    /// </summary>
    /// <param name="services">IServiceCollection 服务集合。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddRfcClient(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RfcOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.Bind(options);
            });

        return AddRfcClientCore(services);
    }

    /// <summary>
    ///   使用 IConfiguration 注册 SAP RFC 客户端服务。
    /// </summary>
    /// <param name="services">IServiceCollection 服务集合。</param>
    /// <param name="configuration">应用程序配置，应包含 RfcOptions 配置节。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddRfcClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RfcOptions>(configuration);
        return AddRfcClientCore(services);
    }

    /// <summary>
    ///   使用委托配置注册 SAP RFC 客户端服务。
    /// </summary>
    /// <param name="services">IServiceCollection 服务集合。</param>
    /// <param name="configureOptions">用于配置 RfcOptions 的委托。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddRfcClient(
        this IServiceCollection services,
        Action<RfcOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        return AddRfcClientCore(services);
    }

    /// <summary>
    ///   使用预配置的 RfcOptions 实例注册 SAP RFC 客户端服务。
    /// </summary>
    /// <param name="services">IServiceCollection 服务集合。</param>
    /// <param name="options">预配置的 RfcOptions 实例。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    public static IServiceCollection AddRfcClient(
        this IServiceCollection services,
        RfcOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.Configure<RfcOptions>(configuredOptions =>
        {
            configuredOptions.ConfigId = options.ConfigId;
            configuredOptions.RfcConnectionConfigs = options.RfcConnectionConfigs;
            configuredOptions.CleanupInterval = options.CleanupInterval;
            configuredOptions.DestinationIdleTimeout = options.DestinationIdleTimeout;
        });

        return AddRfcClientCore(services);
    }

    /// <summary>
    ///   核心注册方法。将 RFC 客户端所需的服务注册到依赖注入容器。
    /// </summary>
    /// <param name="services">IServiceCollection 服务集合。</param>
    /// <returns>服务集合，支持链式调用。</returns>
    private static IServiceCollection AddRfcClientCore(IServiceCollection services)
    {
        services.TryAddSingleton<IRfcConnectionMonitor, RfcConnectionMonitor>();
        services.TryAddSingleton<IRfcConfigProvider>(serviceProvider =>
            new RfcConfigProvider(serviceProvider.GetRequiredService<IOptions<RfcOptions>>()));
        services.TryAddSingleton<IRfcDestinationRegistry, RfcDestinationRegistry>();
        services.TryAddScoped<IRfcClient, RfcClient>();

        return services;
    }
}
