using AgentPrism.Templates.Tests.Infrastructure;

// Cozum bir kez paketlenir ve sablon bir kez kurulur; testler kendi gecici
// dizinlerinde ayri projeler uretir.
[assembly: AssemblyFixture(typeof(TemplateFixture))]
