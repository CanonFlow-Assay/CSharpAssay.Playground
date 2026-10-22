using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Playground.DecisionAssist.Domain;

namespace Playground.DecisionAssist.Cli;

public static class JevAdvice
{
    public static async Task<Advice> RequestAsync(
        HttpClient client, MutationEvidence evidence, string apiKey,
        CancellationToken cancellationToken)
    {
        if (!Assistance.Eligible(evidence))
        {
            return Assistance.Baseline(evidence);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AdviceUnavailable("TYPESAFE_API_KEY is not configured.");
        }

        if (apiKey.Any(char.IsControl))
        {
            return new AdviceUnavailable("Configured credential contains invalid control characters.");
        }

        // This sample sends bounded experiment metadata only, never source text.
        var payload = new
        {
            model = "jev-latest",
            state = new
            {
                task = evidence.Task,
                investigationContext = evidence.InvestigationContext,
                sourceSha256 = evidence.SourceSha256,
                baselinePassed = true,
                mutantCompiled = true,
                testsCompleted = true,
                survived = true,
                covered = evidence.Covered.ToString(),
                hasBoundaryAssertion = evidence.HasBoundaryAssertion.ToString()
            },
            questions = new
            {
                next = new
                {
                    type = "choice",
                    instructions = "Choose the next investigation for this synthetic mutation. Missing facts require review. Do not declare equivalence or a verification verdict.",
                    criteria = new
                    {
                        inspect_assertion = "Inspect the assertion for the declared requirement.",
                        inspect_reachability = "Check whether the selected tests exercise the mutation.",
                        review_contract = "Review expected behavior or possible equivalent behavior.",
                        human_review = "Ask a human for missing context."
                    }
                }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(payload);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new AdviceUnavailable("Rate limited; no retry or fallback was attempted.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AdviceUnavailable("Provider request failed; review configuration or service status.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[65537];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    break;
                }
                length += read;
            }

            return length > 65536
                ? new AdviceUnavailable("Provider response exceeded the size limit.")
                : Parse(evidence, buffer.AsSpan(0, length));
        }
        catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException or IOException)
        {
            return new AdviceUnavailable("Provider transport failed or timed out.");
        }
    }

    public static Advice Parse(MutationEvidence evidence, ReadOnlySpan<byte> response)
    {
        if (!Assistance.Eligible(evidence))
        {
            return Assistance.Baseline(evidence);
        }
        try
        {
            using var json = JsonDocument.Parse(response.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var answer = json.RootElement.GetProperty("answers").GetProperty("next");
            if (answer.TryGetProperty("type", out var type) && type.GetString() != "choice")
            {
                return new ReviewRequired("Provider refused or returned an unsupported answer type.");
            }
            var choice = answer.GetProperty("choice").GetString();
            return choice switch
            {
                "inspect_assertion" => Assistance.Suggest(evidence, NextAction.InspectAssertion),
                "inspect_reachability" => Assistance.Suggest(evidence, NextAction.InspectReachability),
                "review_contract" => Assistance.Suggest(evidence, NextAction.ReviewContract),
                "human_review" => new ReviewRequired("Advisor needs additional evidence or human context."),
                _ => new ReviewRequired("Provider returned an unknown action.")
            };
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new AdviceUnavailable("Provider response did not match the bounded choice contract.");
        }
    }
}
