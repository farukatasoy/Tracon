namespace AgentPrism.Client.Generated;

// NSwag's standard multipart-upload helper type. The generated client
// references FileParameter (AgentPrismUploadAttachmentAsync) but does not
// emit this class itself: NJsonSchema's file-upload detection resolves the
// "file" property's $ref to the IFormFile schema ({type: string, format:
// binary}) far enough to type the parameter as FileParameter, but not far
// enough to also emit the support class NSwag's own template normally
// includes automatically. The shape below is NSwag's own template output,
// copied by hand because that template is not reachable from a $ref'd binary
// schema in this NSwag version (14.2.0). Re-run
// scripts/nswag-postprocess-client.py after every `dotnet nswag run` and
// confirm this class is still needed (grep the regenerated file for "class
// FileParameter"); if a future NSwag version emits it, delete this file.
/// <summary>A file to upload with <see cref="AgentPrismApiClient.AgentPrismUploadAttachmentAsync"/>.</summary>
/// <param name="data">The file content. The caller owns disposal.</param>
/// <param name="fileName">The file name sent as the multipart part's file name.</param>
/// <param name="contentType">The MIME type sent as the multipart part's content type.</param>
public sealed class FileParameter(Stream data, string? fileName = null, string? contentType = null)
{
    /// <summary>The file content.</summary>
    public Stream Data { get; } = data;

    /// <summary>The file name sent as the multipart part's file name.</summary>
    public string? FileName { get; } = fileName;

    /// <summary>The MIME type sent as the multipart part's content type.</summary>
    public string? ContentType { get; } = contentType;
}
