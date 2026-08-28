namespace Isis.Server.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Isis.Core.Enums;

    /// <summary>
    /// Lenient converter for <see cref="MemoryTypeEnum"/>. Accepts any casing of the known values (and a
    /// numeric member value) and maps anything unrecognized, empty, or null to
    /// <see cref="MemoryTypeEnum.Project"/> rather than throwing. Memory 'type' is a soft classification hint;
    /// a valid write (correct slug/categoryId/body) must not be rejected — with a misleading error — merely
    /// because a client supplied an unknown label such as "General" or "text". This keeps less-capable agents
    /// from looping on the memory-upsert call.
    /// </summary>
    public class MemoryTypeEnumConverter : JsonConverter<MemoryTypeEnum>
    {
        /// <inheritdoc />
        public override MemoryTypeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number) && Enum.IsDefined(typeof(MemoryTypeEnum), number))
            {
                return (MemoryTypeEnum)number;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out MemoryTypeEnum parsed) && Enum.IsDefined(typeof(MemoryTypeEnum), parsed))
                {
                    return parsed;
                }
            }

            return MemoryTypeEnum.Project;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, MemoryTypeEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
