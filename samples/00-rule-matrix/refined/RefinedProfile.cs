using System.Collections.Immutable;

namespace Playground.RuleMatrix.Refined.Core
{
    public sealed record RefinedProfile(
        string Name,
        ImmutableArray<string> Aliases)
    {
        public static RefinedProfile Create(string name) =>
            new(name, ImmutableArray<string>.Empty);
    }

    public abstract record BoundaryText
    {
        private BoundaryText()
        {
        }

        public sealed record Missing : BoundaryText;

        public sealed record Present(string Value) : BoundaryText;
    }
}
