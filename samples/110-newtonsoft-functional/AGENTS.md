# Newtonsoft functional boundary sample instructions

This is a clean-room, bounded JSON-boundary comparison, not a rewrite,
assessment, or benchmark of the Newtonsoft.Json library. Preserve the pinned
upstream source reference, package-oracle distinction, MIT attribution, strict
input contract, and the separate ownership of parsing versus pure policy.

Both paths must produce the same canonical result before a timing is accepted.
Do not add a performance threshold, infer general serializer superiority, alter
the input contract to favor one path, or call process-wide allocation retained
or peak memory. Generated reports stay under ignored
`evidence/generated/newtonsoft-functional/`.

Do not vendor or modify upstream source. The local clone is read-only and its
source commit differs from the separately pinned NuGet package oracle. Keep the
sample on the repository's .NET 11 toolchain and preserve nullable and
warnings-as-errors settings.
