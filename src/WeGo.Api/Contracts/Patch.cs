using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeGo.Api.Contracts;

/// <summary>
/// A PATCH field that knows whether the client actually sent it.
/// <para>
/// Without this, "field absent" and "field explicitly null" both arrive as null
/// and a PATCH can never clear a nullable column — or, worse, wipes every field
/// the client did not mention. <see cref="IsSet"/> is false only when the
/// property was missing from the JSON object entirely.
/// </para>
/// </summary>
[JsonConverter(typeof(PatchJsonConverterFactory))]
public readonly struct Patch<T>
{
    private Patch(T? value)
    {
        IsSet = true;
        Value = value;
    }

    /// <summary>True when the property was present in the request body.</summary>
    public bool IsSet { get; }

    /// <summary>The sent value; null when the client sent an explicit JSON null.</summary>
    public T? Value { get; }

    public static Patch<T> Set(T? value) => new(value);

    /// <summary>The effective value: what the client sent, or the current value if untouched.</summary>
    public T? Or(T? current) => IsSet ? Value : current;
}

public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter?)Activator.CreateInstance(typeof(PatchJsonConverter<>).MakeGenericType(valueType));
    }
}

internal sealed class PatchJsonConverter<T> : JsonConverter<Patch<T>>
{
    /// <summary>
    /// Required: <c>Patch&lt;T&gt;</c> is a struct, so without this the
    /// serializer would swallow an explicit null itself and the converter would
    /// never see it — losing the very distinction this type exists to preserve.
    /// </summary>
    public override bool HandleNull => true;

    public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Patch<T>.Set(JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options)
    {
        if (value.IsSet && value.Value is not null)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
            return;
        }

        writer.WriteNullValue();
    }
}
