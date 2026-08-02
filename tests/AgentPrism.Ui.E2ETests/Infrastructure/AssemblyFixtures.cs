using AgentPrism.Ui.E2ETests.Infrastructure;

// Tarayici tum derleme icin bir kez baslar. Her testte yeniden acmak calisma
// suresini birkac kat artirirdi; testler ayri BrowserContext ile yalitilir.
[assembly: AssemblyFixture(typeof(BrowserFixture))]
