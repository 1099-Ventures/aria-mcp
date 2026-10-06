using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks;

public class CodecksFilterKeyConverter : JsonConverter<CodecksFilterKey>
{
	public override CodecksFilterKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		throw new NotImplementedException();
	}

	public override void Write(Utf8JsonWriter writer, CodecksFilterKey value, JsonSerializerOptions options)
	{
		var filterJson = JsonSerializer.Serialize(value.Filter, options);
		var json = $"{value.EntityType}({filterJson})";
		writer.WriteStringValue(json);
	}

	public override void WriteAsPropertyName(Utf8JsonWriter writer, CodecksFilterKey value, JsonSerializerOptions options)
	{
		// This is the key method - serialize the filter and create the property name
		var filterJson = JsonSerializer.Serialize(value.Filter, options);
		var propertyName = $"{value.EntityType}({filterJson})";
		writer.WritePropertyName(propertyName);
	}

	public override CodecksFilterKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		throw new NotImplementedException();
	}
}
