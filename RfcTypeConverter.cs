using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Reflection;

using SAP.Middleware.Connector;

namespace mitzh;

/// <summary>
///   RFC 类型转换器。
///   提供将 SAP RFC 函数参数与 .NET 对象之间进行双向转换的扩展方法。
///   支持日期、时间、结构体、表等 SAP 数据类型的转换。
/// </summary>
public static class RfcTypeConverter
{
    /// <summary>
    ///   SAP 日期格式字符串（yyyyMMdd）。
    /// </summary>
    private const string SapDateFormat = "yyyyMMdd";
    /// <summary>
    ///   SAP 时间格式字符串（hhmmss）。
    /// </summary>
    private const string SapTimeFormat = "hhmmss";
    /// <summary>
    ///   支持的日期解析格式列表。
    /// </summary>
    private static readonly string[] DateFormats =
    {
        "yyyyMMdd",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy.MM.dd",
        "yyyy MM dd",
        "yyyyMMddHHmmss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy.MM.dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy/MM/dd HH:mm:ss.fff",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "MM-dd-yyyy",
        "M-d-yyyy",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy"
    };

    /// <summary>
    ///   支持的时间解析格式列表。
    /// </summary>
    private static readonly string[] TimeFormats =
    {
        "HHmmss",
        "hhmmss",
        "HH:mm:ss",
        "H:mm:ss",
        "HH:mm",
        "H:mm"
    };

    /// <summary>
    ///   将 .NET 对象的属性值设置到 RFC 函数中作为输入参数。
    ///   只处理标记了 ColumnAttribute 的属性，并自动处理日期和时间的格式转换。
    /// </summary>
    /// <typeparam name="T">输入对象的类型。</typeparam>
    /// <param name="function">SAP NCo 的 IRfcFunction 实例。</param>
    /// <param name="input">包含输入参数值的 .NET 对象。</param>
    public static void SetInputValue<T>(this IRfcFunction function, T input)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(input);

        foreach (var property in GetColumnProperties(input.GetType()).Where(t => t.CanRead))
        {
            var key = GetColumnName(property);
            if (key is null)
            {
                continue;
            }

            var value = property.GetValue(input);
            if (value is null or DBNull)
            {
                continue;
            }

            function.SetValue(key, ConvertToSapValue(value));
        }
    }

    /// <summary>
    ///   将字典中的键值设置为 RFC 输入参数。
    /// </summary>
    public static void SetInputValue(this IRfcFunction function, IDictionary input)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(input);

        foreach (DictionaryEntry entry in input)
        {
            if (entry.Key is not string key || string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Dictionary keys must be non-empty strings.", nameof(input));
            }

            if (entry.Value is null or DBNull)
            {
                continue;
            }

            function.SetValue(key, ConvertToSapValue(entry.Value));
        }
    }

    /// <summary>
    ///   从 RFC 函数输出参数中提取值并填充到新的 .NET 对象中。
    ///   支持 EXPORT、CHANGING 和 TABLES 方向参数的自动映射。
    /// </summary>
    /// <typeparam name="T">输出对象的类型，必须包含无参构造函数。</typeparam>
    /// <param name="function">SAP NCo 的 IRfcFunction 实例。</param>
    /// <returns>填充了输出参数值的 .NET 对象。</returns>
    public static T GetOutputValue<T>(this IRfcFunction function)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(function);

        var result = new T();
        var properties = GetColumnProperties(typeof(T))
            .Where(p => p.CanWrite)
            .ToDictionary(GetRequiredColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in function)
        {
            var metaData = parameter.Metadata;
            if (!IsOutputParameter(metaData.Direction) || !properties.TryGetValue(metaData.Name, out var property))
            {
                continue;
            }

            var value = ConvertParameter(parameter, property.PropertyType);
            if (value is not null || IsNullable(property.PropertyType))
            {
                property.SetValue(result, value);
            }
        }

        return result;
    }

    /// <summary>
    ///   获取类型中所有标记了 ColumnAttribute 的公共实例属性。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns>标记了 ColumnAttribute 的属性集合。</returns>
    private static IEnumerable<PropertyInfo> GetColumnProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ColumnAttribute>() is not null);
    }

    /// <summary>
    ///   获取属性上 ColumnAttribute 定义的列名。
    /// </summary>
    /// <param name="property">属性信息。</param>
    /// <returns>ColumnAttribute 定义的名称；未定义有效名称时返回 <see langword="null"/>。</returns>
    private static string GetColumnName(PropertyInfo property)
    {
        var name = property.GetCustomAttribute<ColumnAttribute>()?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    ///   获取属性上 <see cref="ColumnAttribute"/> 定义的有效列名。
    /// </summary>
    /// <param name="property">属性信息。</param>
    /// <returns><see cref="ColumnAttribute"/> 定义的非空名称。</returns>
    /// <exception cref="InvalidOperationException">属性未定义有效列名。</exception>
    private static string GetRequiredColumnName(PropertyInfo property)
    {
        return GetColumnName(property)
            ?? throw new InvalidOperationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' must define a non-empty ColumnAttribute name.");
    }

    /// <summary>
    ///   判断 RFC 参数方向是否为输出类型。
    /// </summary>
    /// <param name="direction">RFC 参数方向。</param>
    /// <returns>如果是 EXPORT、CHANGING 或 TABLES 则返回 true。</returns>
    private static bool IsOutputParameter(RfcDirection direction)
    {
        return direction is RfcDirection.EXPORT or RfcDirection.CHANGING or RfcDirection.TABLES;
    }

    /// <summary>
    ///   将 .NET 值转换为 SAP RFC 兼容的字符串格式。
    ///   处理日期和时间类型的特殊转换。
    /// </summary>
    /// <param name="value">要转换的 .NET 值。</param>
    /// <returns>转换后适用于 RFC 调用的值。</returns>
    private static object ConvertToSapValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.ToString(SapDateFormat, CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString(SapTimeFormat, CultureInfo.InvariantCulture),
            _ => value
        };
    }

    /// <summary>
    ///   将 RFC 参数值转换为 .NET 目标类型。
    /// </summary>
    /// <param name="parameter">RFC 参数。</param>
    /// <param name="targetType">目标 .NET 类型。</param>
    /// <returns>转换后的值。</returns>
    private static object ConvertParameter(IRfcParameter parameter, Type targetType)
    {
        return ConvertRfcValue(
            parameter.Metadata.DataType.ToString(),
            parameter.GetString,
            parameter.GetStructure,
            parameter.GetTable,
            targetType);
    }

    /// <summary>
    ///   将 RFC 结构体字段值转换为 .NET 目标类型。
    /// </summary>
    /// <param name="field">RFC 字段。</param>
    /// <param name="targetType">目标 .NET 类型。</param>
    /// <returns>转换后的值。</returns>
    private static object ConvertField(IRfcField field, Type targetType)
    {
        return ConvertRfcValue(
            field.Metadata.DataType.ToString(),
            field.GetString,
            field.GetStructure,
            field.GetTable,
            targetType);
    }

    /// <summary>
    ///   根据 RFC 数据类型将字符串、结构体或表值转换为 .NET 类型。
    ///   支持 DATE、TIME、STRUCTURE、TABLE 等 SAP 数据类型。
    /// </summary>
    /// <param name="dataType">RFC 数据类型字符串。</param>
    /// <param name="getString">获取字符串值的委托。</param>
    /// <param name="getStructure">获取结构体值的委托。</param>
    /// <param name="getTable">获取表值的委托。</param>
    /// <param name="targetType">目标 .NET 类型。</param>
    /// <returns>转换后的值。</returns>
    private static object ConvertRfcValue(
        string dataType,
        Func<string> getString,
        Func<IRfcStructure> getStructure,
        Func<IRfcTable> getTable,
        Type targetType)
    {
        return dataType switch
        {
            "DATE" => ConvertString(getString(), targetType, DateFormats),
            "TIME" => ConvertString(getString(), targetType, TimeFormats),
            "STRUCTURE" => ConvertStructure(getStructure(), targetType),
            "TABLE" => ConvertTable(getTable(), targetType),
            _ => ConvertString(getString(), targetType)
        };
    }

    /// <summary>
    ///   将 RFC 结构体转换为 .NET 对象或字典。
    ///   若目标类型为 Dictionary&lt;string, object&gt; 则转换为字典，否则按属性映射。
    /// </summary>
    /// <param name="structure">RFC 结构体。</param>
    /// <param name="targetType">目标 .NET 类型。</param>
    /// <returns>转换后的 .NET 对象。</returns>
    private static object ConvertStructure(IRfcStructure structure, Type targetType)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType == typeof(Dictionary<string, object>))
        {
            return StructureToDictionary(structure);
        }

        var result = Activator.CreateInstance(effectiveType)
            ?? throw new InvalidOperationException($"Unable to create '{effectiveType.FullName}'.");

        var properties = GetColumnProperties(effectiveType)
            .Where(p => p.CanWrite)
            .ToDictionary(GetRequiredColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var field in structure)
        {
            if (!properties.TryGetValue(field.Metadata.Name, out var property))
            {
                continue;
            }

            var value = ConvertField(field, property.PropertyType);
            if (value is not null || IsNullable(property.PropertyType))
            {
                property.SetValue(result, value);
            }
        }

        return result;
    }

    /// <summary>
    ///   将 RFC 表（Table）转换为 .NET 数组或列表。
    ///   每行根据行类型转换为对应的 .NET 对象或字典。
    /// </summary>
    /// <param name="table">RFC 表。</param>
    /// <param name="targetType">目标 .NET 类型（数组或 IList）。</param>
    /// <returns>转换后的数组或列表对象。</returns>
    private static object ConvertTable(IRfcTable table, Type targetType)
    {
        var rowType = GetCollectionElementType(targetType) ?? typeof(Dictionary<string, object>);
        var rows = Array.CreateInstance(rowType, table.RowCount);

        for (var i = 0; i < table.RowCount; i++)
        {
            table.CurrentIndex = i;
            rows.SetValue(ConvertStructure(table.CurrentRow, rowType), i);
        }

        if (targetType.IsArray)
        {
            return rows;
        }

        if (typeof(IList).IsAssignableFrom(targetType))
        {
            var list = (IList)Activator.CreateInstance(targetType)
                ?? throw new InvalidOperationException($"Unable to create '{targetType.FullName}'.");
            foreach (var row in rows)
            {
                list.Add(row);
            }

            return list;
        }

        return rows;
    }

    /// <summary>
    ///   将 RFC 结构体转换为字符串键值对字典。
    /// </summary>
    /// <param name="structure">RFC 结构体。</param>
    /// <returns>字段名到值的映射字典。</returns>
    private static Dictionary<string, object> StructureToDictionary(IRfcStructure structure)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in structure)
        {
            result[field.Metadata.Name] = ConvertField(field, typeof(string));
        }

        return result;
    }

    /// <summary>
    ///   获取集合类型（数组、泛型列表等）的元素类型。
    /// </summary>
    /// <param name="targetType">集合类型。</param>
    /// <returns>元素类型；如果无法识别则返回 null。</returns>
    private static Type GetCollectionElementType(Type targetType)
    {
        if (targetType.IsArray)
        {
            return targetType.GetElementType();
        }

        if (targetType.IsGenericType)
        {
            var genericDefinition = targetType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(List<>) || genericDefinition == typeof(IList<>) || genericDefinition == typeof(IEnumerable<>))
            {
                return targetType.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    ///   将 RFC 字符串值转换为 .NET 目标类型。
    ///   支持 DateTime、TimeSpan、枚举以及基础类型的自动转换。
    /// </summary>
    /// <param name="value">RFC 字符串值。</param>
    /// <param name="targetType">目标 .NET 类型。</param>
    /// <param name="formats">可选的日期/时间解析格式数组。</param>
    /// <returns>转换后的值。</returns>
    private static object ConvertString(string value, Type targetType, string[] formats = null)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var text = value?.TrimEnd();
        if (string.IsNullOrWhiteSpace(text))
        {
            return IsNullable(targetType) ? null : GetDefault(effectiveType);
        }

        if (effectiveType == typeof(string))
        {
            return text;
        }

        if (effectiveType == typeof(DateTime))
        {
            var parsed = TryParseDateTime(text, formats ?? DateFormats, out var date);

            return parsed ? date : GetDefaultForInvalidValue(targetType, effectiveType);
        }

        if (effectiveType == typeof(TimeSpan))
        {
            var parsed = TryParseTimeSpan(text, formats ?? TimeFormats, out var time);

            return parsed ? time : GetDefaultForInvalidValue(targetType, effectiveType);
        }

        if (effectiveType.IsEnum)
        {
            return Enum.Parse(effectiveType, text, ignoreCase: true);
        }

        return Convert.ChangeType(text, effectiveType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///   尝试使用多种格式解析日期时间字符串。
    /// </summary>
    /// <param name="value">日期时间字符串。</param>
    /// <param name="formats">支持的格式数组。</param>
    /// <param name="date">解析后的日期时间值。</param>
    /// <returns>解析成功返回 true；否则返回 false。</returns>
    private static bool TryParseDateTime(string value, string[] formats, out DateTime date)
    {
        return DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out date)
            || DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out date)
            || DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out date);
    }

    /// <summary>
    ///   尝试使用多种格式解析时间字符串。
    /// </summary>
    /// <param name="value">时间字符串。</param>
    /// <param name="formats">支持的格式数组。</param>
    /// <param name="time">解析后的时间值。</param>
    /// <returns>解析成功返回 true；否则返回 false。</returns>
    private static bool TryParseTimeSpan(string value, string[] formats, out TimeSpan time)
    {
        return TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out time)
            || TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time)
            || TimeSpan.TryParse(value, CultureInfo.CurrentCulture, out time);
    }

    /// <summary>
    ///   判断类型是否为可空类型（值类型的可空形式或引用类型）。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns>如果是可空类型则返回 true。</returns>
    private static bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }

    /// <summary>
    ///   获取类型的默认值。
    /// </summary>
    /// <param name="type">目标类型。</param>
    /// <returns>值类型的默认实例或引用类型的 null。</returns>
    private static object GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    /// <summary>
    ///   当无法将字符串转换为目标类型时返回默认值。
    ///   对于可空类型返回 null，对于不可空值类型返回其默认值。
    /// </summary>
    /// <param name="targetType">原始目标类型。</param>
    /// <param name="effectiveType">去除了可空包装的有效类型。</param>
    /// <returns>默认值。</returns>
    private static object GetDefaultForInvalidValue(Type targetType, Type effectiveType)
    {
        return IsNullable(targetType) ? null : GetDefault(effectiveType);
    }
}
