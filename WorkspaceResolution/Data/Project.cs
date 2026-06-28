namespace WorkspaceResolution.Data;

public sealed record CsProject(string name, string absoluteFilename, Guid kind, string solutionAbsoluteFilename);