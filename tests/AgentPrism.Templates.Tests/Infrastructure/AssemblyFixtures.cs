using AgentPrism.Templates.Tests.Infrastructure;

// The solution is packed once and the template is installed once; tests
// generate separate projects in their own temp directories.
[assembly: AssemblyFixture(typeof(TemplateFixture))]
