using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Guards;

/// <summary>
/// Measures the allocation cost Phase 140's <c>CallId</c> → tool name map adds
/// to <see cref="ContentGuardMessageMasker"/>'s hot path (Phase 116's gate).
/// </summary>
/// <remarks>
/// The map is built only when the message list carries a
/// <see cref="FunctionResultContent"/>; a plain text conversation — the common
/// case — must not pay for a <see cref="Dictionary{TKey,TValue}"/> it never uses.
/// This is measured relatively (the no-tool-result call must cost strictly less
/// than the tool-result call), not as an absolute zero: the call was never
/// allocation-free to begin with (a <see cref="ContentGuardContext"/> record is
/// built per guard per piece of content regardless of this phase).
/// </remarks>
public sealed class ContentGuardAllocationTests
{
    [Fact]
    public async Task Building_the_tool_name_map_only_allocates_when_a_tool_result_is_present()
    {
        var pipeline = TestData.ContentGuards(guards: StubContentGuard.Blocking("never-matches"));

        IReadOnlyList<ChatMessage> withoutToolResult = [new ChatMessage(ChatRole.User, "hello there")];

        IReadOnlyList<ChatMessage> withToolResult =
        [
            new ChatMessage(ChatRole.User, "hello there"),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "ok")]),
        ];

        // Warm up the JIT for both message-list shapes before measuring either.
        await ContentGuardMessageMasker.MaskAsync(
            pipeline, ContentGuardDirection.Input, withoutToolResult, "model", TestContext.Current.CancellationToken);
        await ContentGuardMessageMasker.MaskAsync(
            pipeline, ContentGuardDirection.Input, withToolResult, "model", TestContext.Current.CancellationToken);

        var beforeWithout = GC.GetAllocatedBytesForCurrentThread();
        await ContentGuardMessageMasker.MaskAsync(
            pipeline, ContentGuardDirection.Input, withoutToolResult, "model", TestContext.Current.CancellationToken);
        var allocatedWithout = GC.GetAllocatedBytesForCurrentThread() - beforeWithout;

        var beforeWith = GC.GetAllocatedBytesForCurrentThread();
        await ContentGuardMessageMasker.MaskAsync(
            pipeline, ContentGuardDirection.Input, withToolResult, "model", TestContext.Current.CancellationToken);
        var allocatedWith = GC.GetAllocatedBytesForCurrentThread() - beforeWith;

        allocatedWithout.ShouldBeLessThan(allocatedWith);
    }
}
