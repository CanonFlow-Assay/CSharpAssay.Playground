using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playground.Saga.Demo;

public sealed record Observation(DateTimeOffset Time, string Partner, string Event, JsonElement Input,
    string State, JsonElement StateData, string Action, JsonElement ActionData, string Evidence, JsonElement EvidenceData);

public sealed record JournalEntry(int Sequence, string PreviousSha256, Observation Observation, string Sha256);

public sealed class EvidenceJournal
{
    internal static readonly string Genesis = new('0', 64);
    private readonly List<JournalEntry> entries = [];

    public ImmutableArray<JournalEntry> Entries => [.. entries];

    public string Head => entries.Count == 0 ? Genesis : entries[^1].Sha256;

    public void Append(Observation observation)
    {
        if (entries.Count != 0 && observation.Time < entries[^1].Observation.Time)
        {
            throw new InvalidDataException("Journal observation time moved backwards.");
        }

        var sequence = entries.Count + 1;
        var previous = Head;
        entries.Add(new(sequence, previous, observation, Hash(sequence, previous, observation)));
    }

    public static bool Verify(IEnumerable<JournalEntry> entries, string expectedHead)
    {
        var sequence = 0;
        var previous = Genesis;
        var lastTime = DateTimeOffset.MinValue;
        foreach (var entry in entries)
        {
            sequence++;
            if (entry.Sequence != sequence || entry.PreviousSha256 != previous || entry.Observation.Time < lastTime ||
                !HasValidHash(entry))
            {
                return false;
            }

            previous = entry.Sha256;
            lastTime = entry.Observation.Time;
        }

        return previous == expectedHead;
    }

    internal static bool HasValidHash(JournalEntry entry) =>
        entry.Sha256 == Hash(entry.Sequence, entry.PreviousSha256, entry.Observation);

    private static string Hash(int sequence, string previous, Observation observation)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "saga-assay-journal-entry/2");
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("previousSha256", previous);
            writer.WriteString("time", observation.Time.UtcDateTime);
            writer.WriteString("partner", observation.Partner);
            writer.WriteString("event", observation.Event);
            WritePayload(writer, "input", observation.Input);
            writer.WriteString("state", observation.State);
            WritePayload(writer, "stateData", observation.StateData);
            writer.WriteString("action", observation.Action);
            WritePayload(writer, "actionData", observation.ActionData);
            writer.WriteString("evidence", observation.Evidence);
            WritePayload(writer, "evidenceData", observation.EvidenceData);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WritePayload(Utf8JsonWriter writer, string name, JsonElement value)
    {
        writer.WritePropertyName(name);
        WriteValue(writer, value);
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    WritePayload(writer, property.Name, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) { WriteValue(writer, item); }
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
