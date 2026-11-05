namespace Tracon.Samples.CustomModelProvider.Tests;

/// <summary>
/// The sample reports the key it was set up with, so that Tracon replaces it wherever the text of a failure quotes it.
/// </summary>
public sealed class ContosoSetupKeyTests
{
    [Fact]
    public void The_provider_reports_the_key_it_was_set_up_with_and_no_other()
    {
        var provider = new ContosoModelProvider("contoso-setup-key-0123456789");

        provider.SetupApiKeys.ShouldBe(["contoso-setup-key-0123456789"]);
    }
}
