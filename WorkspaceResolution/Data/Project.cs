namespace WorkspaceResolution.Data;

public readonly record struct CsProject(string name, string directory, string absoluteFilename, Guid kind);