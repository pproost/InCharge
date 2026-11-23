using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InCharge.Mobile.Services;

// Log entries stored before contract v1 have local times without an offset. Those are read
// as local time, which applies the device time zone's offset for that instant (DST included).
public class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : throw new JsonException($"Invalid timestamp '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}
