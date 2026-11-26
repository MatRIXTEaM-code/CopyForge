using System.Collections.Generic;

namespace CopyForge.Engine.Net;

public sealed record SendResult(int Files, int Sent, int Failed, long Bytes, IReadOnlyList<string> Errors);
