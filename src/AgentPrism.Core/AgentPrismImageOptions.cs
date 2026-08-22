namespace AgentPrism;

/// <summary>Settings for the image-generation tool.</summary>
/// <remarks>
/// The tool is off by default. Setting <see cref="Enabled"/> to <see langword="true"/>
/// registers <c>generate_image</c> only when an <see cref="Microsoft.Extensions.AI.IImageGenerator"/>
/// is also registered.
/// </remarks>
public sealed class AgentPrismImageOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "AgentPrism:Images";

    /// <summary>Gets or sets whether the image tool and operator endpoint are enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the provider name used to find configured image prices.</summary>
    /// <remarks>
    /// Provider extensions set this value when it is empty. Set it explicitly when
    /// registering a custom <see cref="Microsoft.Extensions.AI.IImageGenerator"/>.
    /// </remarks>
    public string? Provider { get; set; }

    /// <summary>Gets or sets the image model.</summary>
    /// <remarks>
    /// Image model names are not inferred from a chat model. Providers use distinct
    /// model catalogues, and an inferred name could spend money on the wrong model.
    /// </remarks>
    public string? Model { get; set; }

    /// <summary>Gets or sets the most images one call may produce. Default is one.</summary>
    public int MaxImagesPerRequest { get; set; } = 1;
}

/// <summary>Validates <see cref="AgentPrismImageOptions"/> during application startup.</summary>
internal sealed class AgentPrismImageOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<AgentPrismImageOptions>
{
    /// <inheritdoc />
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, AgentPrismImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.MaxImagesPerRequest < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismImageOptions)}.{nameof(AgentPrismImageOptions.MaxImagesPerRequest)} must be at least 1.");
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismImageOptions)}.{nameof(AgentPrismImageOptions.Model)} is required when " +
                $"{nameof(AgentPrismImageOptions.Enabled)} is true.");
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Provider))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismImageOptions)}.{nameof(AgentPrismImageOptions.Provider)} is required when " +
                $"{nameof(AgentPrismImageOptions.Enabled)} is true.");
        }

        return failures is null
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
            : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(failures);
    }
}
