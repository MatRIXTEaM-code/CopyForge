using System;

namespace CopyForge.Engine;

public sealed record JobRecord(DateTime Started, JobOptions Options, JobResult? Result, string? Problem);
