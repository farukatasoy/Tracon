namespace Tracon;

/// <summary>Where a logical <c>runs</c> reader column comes from.</summary>
internal enum RunColumnSource
{
    /// <summary>The run's own row (<c>r.&lt;column&gt;</c>).</summary>
    Own,

    /// <summary>An aggregate over the run's descendant tree.</summary>
    Tree,
}

/// <summary>One logical column of the <c>runs</c> reader, in ordinal order.</summary>
/// <param name="Name">The bare column or aggregate name (without the <c>r.</c>/<c>tree.</c> prefix).</param>
/// <param name="Source">Whether the value comes from the run's own row or its descendant tree.</param>
internal readonly record struct RunColumn(string Name, RunColumnSource Source);
