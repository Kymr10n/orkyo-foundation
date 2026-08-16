using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Models;

/// <summary>
/// A field on a patch request that can tell "not mentioned" from "explicitly set to nothing".
///
/// A plain nullable cannot: absent and null arrive identically, so a request has no way to say
/// "erase this". That is fine for a string, where the empty string is a usable "no value"
/// sentinel, and impossible for an id — there is no empty Guid that does not also mean a real
/// value. Fields of that shape carry <see cref="Optional{T}"/> so an update can clear them.
///
/// Absent leaves the column alone. Present-and-null writes NULL. This is the JSON Merge Patch
/// (RFC 7396) reading of null, applied only where the ambiguity actually bites.
/// </summary>
[JsonConverter(typeof(OptionalConverterFactory))]
public readonly struct Optional<T>
{
    private Optional(T? value)
    {
        IsPresent = true;
        Value = value;
    }

    /// <summary>False when the property was absent from the request body.</summary>
    public bool IsPresent { get; }

    /// <summary>The supplied value. Null when the caller asked for the field to be cleared.</summary>
    public T? Value { get; }

    public static Optional<T> Of(T? value) => new(value);

    /// <summary>Absent — the caller did not mention this field.</summary>
    public static Optional<T> Absent => default;

    /// <summary>True when the caller named the field and asked for it to hold nothing.</summary>
    public bool IsCleared => IsPresent && Value is null;

    public static implicit operator Optional<T>(T? value) => Of(value);
}

/// <summary>
/// Binds <see cref="Optional{T}"/> for any T. Absent properties never reach a converter, so the
/// struct's default (not present) is the correct answer for them by construction.
/// </summary>
public sealed class OptionalConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(OptionalConverter<>).MakeGenericType(valueType))!;
    }
}

internal sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Reaching the converter at all means the property was present, so a null token is the
        // caller asking to clear rather than the absence of an answer.
        if (reader.TokenType == JsonTokenType.Null) return Optional<T>.Of(default);
        return Optional<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.Value is null) writer.WriteNullValue();
        else JsonSerializer.Serialize(writer, value.Value, options);
    }
}
