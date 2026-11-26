using System;
using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed record JobResult(long FilesProcessed, long FilesSkipped, long FilesDeleted, long FilesFailed, long BytesTransferred, TimeSpan Elapsed, bool Cancelled, IReadOnlyList<string> Errors, IReadOnlyList<FilePair>? FailedPairs = null, PlanPreview? Preview = null);
