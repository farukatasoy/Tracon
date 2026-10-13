namespace Tracon;

/// <summary>Where the content of a skill script came from.</summary>
/// <remarks>
/// An executor can refuse an origin it does not support: for example, a remote
/// runner that only takes content-pinned scripts throws for
/// <see cref="File"/>. The model then sees a failed call.
/// </remarks>
public enum SkillScriptOrigin
{
    /// <summary>
    /// A script file under a skill root on the host's disk. Its content is part of
    /// the deployment and is not pinned to a grant;
    /// <see cref="SkillScriptExecutionRequest.SourcePath"/> names the file.
    /// </summary>
    File = 0,

    /// <summary>
    /// A script stored in the database or registered in code. It runs only under a
    /// grant pinned to its exact content, and the content travels in the request.
    /// </summary>
    Stored = 1,
}
