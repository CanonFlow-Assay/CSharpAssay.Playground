using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playground.Saga.Demo;

public sealed record Observation(DateTimeOffset Time, string Partner, string Event, JsonElement Input,
    string State, string Action, JsonElement ActionData, string Evidence);

public sealed record JournalEntry(int Sequence, string PreviousSha256, Observation Observation, string Sha256);

public sealed class EvidenceJournal
{
    private static readonly string Genesis = new('0', 64);
    private readonly List<JournalEntry> entries = [];

    public ImmutableArray<JournalEntry> Entries => [.. entries];

    public string Head => entries.Count == 0 ? Genesis : entries[^1].Sha256;

    public void Append(Observation observation)
    {
        var sequence = entries.Count + 1;
        var previous = Head;
        entries.Add(new(sequence, previous, observation, Hash(sequence, previous, observation)));
    }

    public static bool Verify(IEnumerable<JournalEntry> entries, string expectedHead)
    {
        var sequence = 0;
        var previous = Genesis;
        foreach (var entry in entries)
        {
            sequence++;
            if (entry.Sequence != sequence || entry.PreviousSha256 != previous ||
                entry.Sha256 != Hash(sequence, previous, entry.Observation))
            {
                return false;
            }

            previous = entry.Sha256;
        }

        return previous == expectedHead;
    }

    private static string Hash(int sequence, string previous, Observation observation) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "saga-assay-journal-entry/1",
            Sequence = sequence,
            PreviousSha256 = previous,
            Observation = observation
        })));
}
