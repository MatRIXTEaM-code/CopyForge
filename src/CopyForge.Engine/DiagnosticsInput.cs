using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed record DiagnosticsInput(string AppVersion, JobRecord? LastJob, JobOptions Current, IReadOnlyList<string> EventLog, IReadOnlyList<string> Crashes, string CrashLogPath);
