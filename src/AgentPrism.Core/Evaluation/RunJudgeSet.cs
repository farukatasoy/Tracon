namespace AgentPrism;

/// <summary>Validates the registered judge set during host startup.</summary>
internal sealed class RunJudgeSet
{
    public RunJudgeSet(IEnumerable<IRunJudge> judges)
    {
        ArgumentNullException.ThrowIfNull(judges);

        foreach (var judge in judges)
        {
            if (string.IsNullOrWhiteSpace(judge.Name) ||
                judge.Name.Length > 64 ||
                judge.Name.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            {
                throw new AgentPrismException(
                    "Each IRunJudge.Name must match [A-Za-z0-9._-]{1,64}.");
            }
        }

        var duplicate = judges
            .GroupBy(static judge => judge.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new AgentPrismException($"More than one run judge named '{duplicate.Key}' has been registered.");
        }
    }
}
