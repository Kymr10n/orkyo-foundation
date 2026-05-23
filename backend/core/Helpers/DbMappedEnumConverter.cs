using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Helpers;

/// <summary>
/// A JsonConverter that delegates serialization to EnumMapper.ToDbValue / FromDbValue.
/// This correctly honors [JsonStringEnumMemberName] attributes regardless of which
/// global JsonStringEnumConverter instance is registered in options.
/// </summary>
public sealed class DbMappedEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString()
            ?? throw new JsonException($"Expected a JSON string for enum type {typeof(T).Name}.");
        return EnumMapper.FromDbValue<T>(str);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(EnumMapper.ToDbValue(value));
    }
}
