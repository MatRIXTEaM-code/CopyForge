namespace CopyForge.Engine;

public sealed record WatchEvent(string Kind, string Text, JobResult? Result = null);
