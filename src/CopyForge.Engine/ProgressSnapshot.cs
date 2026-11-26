using System;

namespace CopyForge.Engine;

public readonly record struct ProgressSnapshot(string Phase, long FilesTotal, long FilesDone, long BytesTotal, long BytesDone, long FilesSkipped, long FilesFailed, long FilesDeleted, string CurrentFile, TimeSpan Elapsed, double BytesPerSecond, double Fraction, TimeSpan? Eta);
