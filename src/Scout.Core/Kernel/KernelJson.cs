using System.Text.Json;
using System.Text.Json.Serialization;
using Scout.Core.Ir;

namespace Scout.Core.Kernel;

internal static class KernelJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    internal static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, value.GetType(), Options) + "\n";
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new StructuralIdJsonConverter());
        options.Converters.Add(new ConfidenceScoreJsonConverter());
        return options;
    }

    private sealed class StructuralIdJsonConverter : JsonConverter<StructuralId>
    {
        public override StructuralId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new StructuralId(reader.GetString() ?? throw new JsonException("A structural identifier must be a string."));
        }

        public override void Write(Utf8JsonWriter writer, StructuralId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class ConfidenceScoreJsonConverter : JsonConverter<ConfidenceScore>
    {
        public override ConfidenceScore Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new ConfidenceScore(reader.GetDouble());
        }

        public override void Write(Utf8JsonWriter writer, ConfidenceScore value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}
