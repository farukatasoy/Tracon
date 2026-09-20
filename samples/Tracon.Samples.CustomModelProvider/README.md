# Custom model provider sample

This sample implements `IModelProvider` and a small `IChatClient` without depending
on `Tracon.Core`. Its sibling test project runs the public model-provider contract.

The projects reference packed NuGet artifacts, not Tracon source projects. Run them
through the repository's release gate as described in [`../README.md`](../README.md).

