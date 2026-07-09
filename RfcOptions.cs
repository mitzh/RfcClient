using System.Data.Common;

namespace RfcClient;

/// <summary>
///   SAP RFC 客户端选项配置。
///   包含默认配置标识、连接配置集合以及连接管理器的清理参数。
/// </summary>
public class RfcOptions
{
    /// <summary>
    ///   获取或设置默认的 RFC 配置标识。
    /// </summary>
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>
    ///   获取或设置 RFC 连接配置列表。
    /// </summary>
    public List<RfcConnectionConfig> RfcConnectionConfigs { get; set; } = new();

    /// <summary>
    ///   获取或设置连接管理器中清理定时器的执行间隔。
    ///   默认值为 5 分钟。
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///   获取或设置连接管理器中目标的空闲超时时间。
    ///   超过此时间未使用的连接将被清理。默认值为 10 分钟。
    /// </summary>
    public TimeSpan DestinationIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///   获取默认的配置标识。
    ///   如果显式设置了 ConfigId 则返回该值；
    ///   否则从 RfcConnectionConfigs 中查找标记为 IsDefault 的项；
    ///   如果未标记默认项，则返回列表中的第一个配置标识。
    /// </summary>
    /// <returns>默认配置标识字符串。</returns>
    public string GetConfigId()
    {
        ValidateConfigIds();

        if (!string.IsNullOrWhiteSpace(ConfigId))
        {
            if (RfcConnectionConfigs.Any(config => IsSameConfigId(config.ConfigId, ConfigId)))
            {
                return ConfigId;
            }
            // 未匹配到 ConfigId 时，回退到默认配置或第一项
        }

        var defaultConfigs = RfcConnectionConfigs.Where(config => config.IsDefault).ToArray();
        if (defaultConfigs.Length > 1)
        {
            throw new InvalidOperationException("Only one RfcConnectionConfigs item can be marked as IsDefault.");
        }

        return defaultConfigs.Length == 1
            ? defaultConfigs[0].ConfigId
            : RfcConnectionConfigs[0].ConfigId;
    }

    /// <summary>
    ///   获取所有 RFC 连接配置的只读字典，以配置标识为键。
    /// </summary>
    /// <returns>包含所有 RFC 连接参数的只读字典。</returns>
    public IReadOnlyDictionary<string, RfcConfigParameter> GetRfcDestinations()
    {
        ValidateConfigIds();

        var destinations = new Dictionary<string, RfcConfigParameter>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in RfcConnectionConfigs)
        {
            destinations.Add(config.ConfigId, config.ToRfcConfigParameter());
        }

        return destinations;
    }

    /// <summary>
    ///   根据指定配置标识获取对应的 RFC 连接参数。
    /// </summary>
    /// <param name="configId">RFC 配置标识。</param>
    /// <returns>对应的 RfcConfigParameter 对象。</returns>
    public RfcConfigParameter GetRfcOptions(string configId)
    {
        if (string.IsNullOrWhiteSpace(configId))
        {
            throw new ArgumentNullException(nameof(configId));
        }

        var destinations = GetRfcDestinations();
        if (destinations.TryGetValue(configId, out var options))
        {
            return options;
        }

        throw new InvalidOperationException(
            $"SAP RFC config '{configId}' was not found in RfcConnectionConfigs.");
    }

    /// <summary>
    ///   验证连接配置列表的合法性。
    ///   确保列表非空、所有配置标识不为空且无重复。
    /// </summary>
    private void ValidateConfigIds()
    {
        if (RfcConnectionConfigs.Count == 0)
        {
            throw new InvalidOperationException("At least one RfcConnectionConfigs item is required.");
        }

        var configIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in RfcConnectionConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.ConfigId))
            {
                throw new InvalidOperationException("RfcConnectionConfigs contains an item with empty ConfigId.");
            }

            if (!configIds.Add(config.ConfigId))
            {
                throw new InvalidOperationException(
                    $"RfcConnectionConfigs contains duplicate ConfigId '{config.ConfigId}'.");
            }
        }
    }

    /// <summary>
    ///   比较两个配置标识是否相同（忽略大小写）。
    /// </summary>
    /// <param name="left">左侧配置标识。</param>
    /// <param name="right">右侧配置标识。</param>
    /// <returns>如果忽略大小写后相同则返回 true。</returns>
    private static bool IsSameConfigId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
///   RFC 连接配置项。
///   包含单个 SAP RFC 连接的配置标识、默认标记和连接字符串。
/// </summary>
public class RfcConnectionConfig
{
    /// <summary>
    ///   获取或设置配置标识。用于在配置集合中唯一标识此连接。
    /// </summary>
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>
    ///   获取或设置一个值，指示此配置是否为默认配置。
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    ///   获取或设置 SAP RFC 连接字符串。
    ///   包含服务器地址、登录凭据等连接参数。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    ///   将当前连接配置转换为 <see cref="RfcConfigParameter"/> 对象。
    /// </summary>
    /// <returns>转换后的 RfcConfigParameter 实例。</returns>
    public RfcConfigParameter ToRfcConfigParameter()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException($"ConnectionString is required for SAP RFC config '{ConfigId}'.");
        }

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = ConnectionString
        };

        return new RfcConfigParameter
        {
            ApplicationServer = GetString(builder, nameof(RfcConfigParameter.ApplicationServer), "Server"),
            SystemNumber = GetString(builder, nameof(RfcConfigParameter.SystemNumber), defaultValue: "00"),
            SystemId = GetString(builder, nameof(RfcConfigParameter.SystemId)),
            Client = GetString(builder, nameof(RfcConfigParameter.Client)),
            UserName = GetString(builder, nameof(RfcConfigParameter.UserName), "User ID", "UserId"),
            Password = GetString(builder, nameof(RfcConfigParameter.Password)),
            Language = GetString(builder, nameof(RfcConfigParameter.Language), defaultValue: "ZH"),
            PoolSize = GetInt(builder, nameof(RfcConfigParameter.PoolSize), defaultValue: 5),
            MaxPoolSize = GetInt(
                builder,
                nameof(RfcConfigParameter.MaxPoolSize),
                aliases: new[] { "Max Pool Size" },
                defaultValue: 10),
            ConnectionTimeout = GetInt(builder, nameof(RfcConfigParameter.ConnectionTimeout), defaultValue: 30),
            CommunicationTimeout = GetInt(builder, nameof(RfcConfigParameter.CommunicationTimeout), defaultValue: 60),
            MessageServerHost = GetString(builder, nameof(RfcConfigParameter.MessageServerHost)),
            MessageServerService = GetString(builder, nameof(RfcConfigParameter.MessageServerService)),
            MessageServerPort = GetInt(builder, nameof(RfcConfigParameter.MessageServerPort), defaultValue: 3600)
        };
    }

    /// <summary>
    ///   从连接字符串构建器中获取字符串值。
    /// </summary>
    /// <param name="builder">数据库连接字符串构建器。</param>
    /// <param name="key">主键名。</param>
    /// <param name="aliases">可选别名列表。</param>
    /// <returns>获取到的字符串值，未找到时返回空字符串。</returns>
    private static string GetString(
        DbConnectionStringBuilder builder,
        string key,
        params string[] aliases)
    {
        return GetString(builder, key, aliases, string.Empty);
    }

    /// <summary>
    ///   从连接字符串构建器中获取字符串值，可指定默认值。
    /// </summary>
    /// <param name="builder">数据库连接字符串构建器。</param>
    /// <param name="key">主键名。</param>
    /// <param name="aliases">可选别名列表。</param>
    /// <param name="defaultValue">默认值，当未找到键时返回。</param>
    /// <returns>获取到的字符串值，未找到时返回默认值。</returns>
    private static string GetString(
        DbConnectionStringBuilder builder,
        string key,
        string[] aliases = null,
        string defaultValue = "")
    {
        if (TryGetValue(builder, key, aliases, out var value))
        {
            return value?.ToString() ?? string.Empty;
        }

        return defaultValue;
    }

    /// <summary>
    ///   从连接字符串构建器中获取整数值。
    /// </summary>
    /// <param name="builder">数据库连接字符串构建器。</param>
    /// <param name="key">主键名。</param>
    /// <param name="aliases">可选别名列表。</param>
    /// <returns>获取到的整数值，未找到时返回 0。</returns>
    private static int GetInt(
        DbConnectionStringBuilder builder,
        string key,
        params string[] aliases)
    {
        return GetInt(builder, key, aliases, 0);
    }

    /// <summary>
    ///   从连接字符串构建器中获取整数值，可指定默认值。
    /// </summary>
    /// <param name="builder">数据库连接字符串构建器。</param>
    /// <param name="key">主键名。</param>
    /// <param name="aliases">可选别名列表。</param>
    /// <param name="defaultValue">默认值，当未找到键时返回。</param>
    /// <returns>获取到的整数值，未找到时返回默认值。</returns>
    private static int GetInt(
        DbConnectionStringBuilder builder,
        string key,
        string[] aliases = null,
        int defaultValue = 0)
    {
        if (!TryGetValue(builder, key, aliases, out var value))
        {
            return defaultValue;
        }

        if (int.TryParse(value?.ToString(), out var intValue))
        {
            return intValue;
        }

        throw new InvalidOperationException($"ConnectionString parameter '{key}' must be an integer.");
    }

    /// <summary>
    ///   尝试从连接字符串构建器中获取指定键的值，支持多别名。
    /// </summary>
    /// <param name="builder">数据库连接字符串构建器。</param>
    /// <param name="key">主键名。</param>
    /// <param name="aliases">可选别名列表。</param>
    /// <param name="value">获取到的值。</param>
    /// <returns>如果找到键对应的值则返回 true；否则返回 false。</returns>
    private static bool TryGetValue(
        DbConnectionStringBuilder builder,
        string key,
        string[] aliases,
        out object value)
    {
        var keys = aliases is null ? new[] { key } : aliases.Prepend(key);
        foreach (var candidateKey in keys)
        {
            if (builder.TryGetValue(candidateKey, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }
}
