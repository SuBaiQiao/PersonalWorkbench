namespace PersonalWorkbench.Models;

public sealed class CollaborativeTextUpdateRequest
{
    public string Text { get; set; } = string.Empty;
}

public sealed record CollaborativeEditorState(
    string Text,
    IReadOnlyList<string> History,
    IReadOnlyList<string> Files);

public sealed record CollaborativeUploadResponse(
    string FileName,
    string Message);
