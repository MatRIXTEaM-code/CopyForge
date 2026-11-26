using System.Collections.Generic;
using System.Linq;

namespace CopyForge.Engine;

public static class JobResults
{
	/// <summary>Adds the counts of <paramref name="b"/> to <paramref name="a"/> (used for multi-destination jobs).</summary>
	public static JobResult Merge(JobResult a, JobResult b)
	{
		List<string> errors = a.Errors.Concat(b.Errors).ToList();
		return new JobResult(a.FilesProcessed + b.FilesProcessed, a.FilesSkipped + b.FilesSkipped, a.FilesDeleted + b.FilesDeleted, a.FilesFailed + b.FilesFailed, a.BytesTransferred + b.BytesTransferred, a.Elapsed + b.Elapsed, a.Cancelled || b.Cancelled, errors, a.FailedPairs, a.Preview);
	}
}
