# Scheduling decisions around time zones

`TimeZoneBoundary` owns host time-zone lookups and DST inspection. It turns that
effect into facts; the pure policy then schedules one valid local time, rejects a
skipped local time, or requires an explicit choice for an ambiguous one.

The verification cases inject DST facts so they are reproducible on every host.
This is not a recurring-calendar, locale, persistence, or time-zone-data
qualification. A caller must choose and record an offset for an ambiguous time.

```bash
dotnet run --project samples/150-scheduling-decisions -c Release -- --verify
```
