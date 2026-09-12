namespace Tracon;

/// <summary>Validates the registered judge set during host startup.</summary>
internal sealed class RunJudgeSet
{
    public RunJudgeSet(IEnumerable<IRunJudge> judges)
    {
        ArgumentNullException.ThrowIfNull(judges);

        foreach (var judge in judges)
        {
            // 🚨 The rule is NOT written a second time here. A judge writes its
            // own name into the score it produces, so a name this gate accepts
            // must also be a name RunScoreRules accepts -- two spellings of one
            // rule would eventually let a registered judge fail at the write.
            if (!RunScoreRules.IsValidName(judge.Name))
            {
                throw new TraconException(
                    "Each IRunJudge.Name must match [A-Za-z0-9._-]{1,64}.");
            }
        }

        var duplicate = judges
            .GroupBy(static judge => judge.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new TraconException($"More than one run judge named '{duplicate.Key}' has been registered.");
        }
    }
}
