using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace CopyForge.Engine;

internal sealed class AlignedBuffer : MemoryManager<byte>
{
	public const int Alignment = 4096;

	private unsafe byte* _p;

	private readonly int _len;

	public int Length => _len;

	public unsafe AlignedBuffer(int length)
	{
		_len = length;
		_p = (byte*)NativeMemory.AlignedAlloc((nuint)length, 4096u);
	}

	public unsafe override Span<byte> GetSpan()
	{
		return new Span<byte>(_p, _len);
	}

	public unsafe override MemoryHandle Pin(int elementIndex = 0)
	{
		return new MemoryHandle(_p + elementIndex);
	}

	public override void Unpin()
	{
	}

	public void Free()
	{
		Dispose(disposing: true);
	}

	protected unsafe override void Dispose(bool disposing)
	{
		if (_p != null)
		{
			NativeMemory.AlignedFree(_p);
			_p = null;
		}
	}
}
