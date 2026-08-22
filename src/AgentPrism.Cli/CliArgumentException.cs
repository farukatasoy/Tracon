namespace AgentPrism.Cli;

/// <summary>A command line argument is missing or invalid. Caught by <see cref="Program"/> and printed without a stack trace.</summary>
internal sealed class CliArgumentException(string message) : Exception(message);
