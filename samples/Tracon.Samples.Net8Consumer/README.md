# .NET 8 consumer sample

A console application that targets `net8.0`, takes `Tracon.Core` from a NuGet
feed, and runs one agent: generic host, a model provider, an agent definition,
the agent catalog, and a real run. The model answers locally and makes no
network call.

The Tracon packages ship `net8.0`, `net9.0`, and `net10.0` builds. The other
samples run on `net10.0`; this one restores the `net8.0` dependency group and
runs on the .NET 8 runtime. It exits with a non-zero code on any other runtime,
so a success message is evidence that the `net8.0` build works.

The project references packed NuGet artifacts, not Tracon source projects. Run it
through the repository's release gate as described in [`../README.md`](../README.md).
