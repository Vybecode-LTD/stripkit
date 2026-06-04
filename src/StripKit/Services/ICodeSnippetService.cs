using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Generates ready-to-paste loader code that wires an exported filmstrip into a target
/// framework, so the user doesn't have to write the boilerplate by hand. Pure string
/// generation (no Skia / no Avalonia); <see cref="SaveAsync"/> is the only I/O.
/// </summary>
public interface ICodeSnippetService
{
    /// <summary>Generates the loader snippet for <paramref name="target"/>.</summary>
    string Generate(CodeTarget target, CodeSnippetRequest request);

    /// <summary>The on-disk file name a snippet for <paramref name="target"/> should use,
    /// e.g. <c>filterCutoff.LookAndFeel.h</c>.</summary>
    string FileName(CodeTarget target, string controlId);

    /// <summary>Writes the snippet for <paramref name="target"/> into <paramref name="directory"/>
    /// (using <see cref="FileName"/>) and returns the full path written.</summary>
    Task<string> SaveAsync(CodeTarget target, CodeSnippetRequest request, string directory);
}
