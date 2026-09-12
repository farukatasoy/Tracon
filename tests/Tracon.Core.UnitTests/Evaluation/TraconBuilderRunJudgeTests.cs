using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Evaluation;

public sealed class TraconBuilderRunJudgeTests
{
    [Fact]
    public void Generic_registration_is_singleton_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddRunJudge<CountingJudge>()
            .AddRunJudge<CountingJudge>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IRunJudge>().OfType<CountingJudge>().ShouldHaveSingleItem();
        var second = provider.GetServices<IRunJudge>().OfType<CountingJudge>().ShouldHaveSingleItem();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Instance_registration_preserves_multiple_configurations_and_identity()
    {
        var first = new NamedJudge("first");
        var second = new NamedJudge("second");
        var services = new ServiceCollection();
        services.AddTracon().AddRunJudge(first).AddRunJudge(second);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetServices<IRunJudge>().OfType<NamedJudge>().ToArray();

        resolved.ShouldBe([first, second]);
    }

    [Fact]
    public void Factory_registration_runs_once_and_preserves_multiple_configurations()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddTracon()
            .AddRunJudge(_ =>
            {
                calls++;
                return new NamedJudge("first");
            })
            .AddRunJudge(_ => new NamedJudge("second"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IRunJudge>().OfType<NamedJudge>()
            .Select(static judge => judge.Name)
            .ShouldBe(["first", "second"]);
        provider.GetServices<IRunJudge>().OfType<NamedJudge>().Count().ShouldBe(2);
        calls.ShouldBe(1);
    }

    [Fact]
    public void AddModelRunJudge_uses_the_same_idempotent_registration_primitive()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddModelRunJudge(static _ => { })
            .AddModelRunJudge(static _ => { });
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IRunJudge>().OfType<ModelRunJudge>().Count().ShouldBe(1);
    }

    private sealed class CountingJudge : IRunJudge
    {
        public string Name => "counting";

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(new RunJudgment());
    }

    private sealed class NamedJudge(string name) : IRunJudge
    {
        public string Name { get; } = name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(new RunJudgment());
    }
}
