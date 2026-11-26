using System.Collections.Generic;

namespace CopyForge.Engine;

public sealed record PlanPreview(long CopyCount, long CopyBytes, long SkipCount, long DeleteCount, IReadOnlyList<string> CopyList, IReadOnlyList<string> DeleteList, bool Truncated);
