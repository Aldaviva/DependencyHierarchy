namespace WorkspaceResolution.Data;

public record CsProject(string name, string absoluteFilename, Guid kind, string solutionAbsoluteFilename);