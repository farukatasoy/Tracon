using Tracon.Ui.E2ETests.Infrastructure;

// The browser starts once for the whole assembly. Reopening it for every test
// would multiply the run time; tests are isolated via a separate BrowserContext.
[assembly: AssemblyFixture(typeof(BrowserFixture))]
