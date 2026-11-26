using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace CopyForge.Engine;

/// <summary>BLAKE3 (default 256-bit hash mode), managed implementation. Checked against the reference implementation in the smoke tests.</summary>
internal sealed class Blake3
{
	private static readonly uint[] IV = new uint[8] { 1779033703u, 3144134277u, 1013904242u, 2773480762u, 1359893119u, 2600822924u, 528734635u, 1541459225u };

	private static readonly byte[] Perm = new byte[16] { 2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8 };

	private const uint ChunkStart = 1u;

	private const uint ChunkEnd = 2u;

	private const uint Parent = 4u;

	private const uint Root = 8u;

	private readonly uint[] _cv = new uint[8];

	private readonly byte[] _buf = new byte[64];

	private int _bufLen;

	private int _blocks;

	private ulong _chunkCounter;

	private readonly uint[][] _stack = new uint[56][];

	private int _stackLen;

	public Blake3()
	{
		Array.Copy(IV, _cv, 8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void G(Span<uint> s, int a, int b, int c, int d, uint mx, uint my)
	{
		s[a] = s[a] + s[b] + mx;
		s[d] = BitOperations_RotR(s[d] ^ s[a], 16);
		s[c] += s[d];
		s[b] = BitOperations_RotR(s[b] ^ s[c], 12);
		s[a] = s[a] + s[b] + my;
		s[d] = BitOperations_RotR(s[d] ^ s[a], 8);
		s[c] += s[d];
		s[b] = BitOperations_RotR(s[b] ^ s[c], 7);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint BitOperations_RotR(uint v, int n)
	{
		return (v >> n) | (v << (32 - n));
	}

	private static void Compress(ReadOnlySpan<uint> cv, ReadOnlySpan<uint> block, ulong counter, uint blockLen, uint flags, Span<uint> outWords)
	{
		Span<uint> s = stackalloc uint[16];
		Span<uint> m = stackalloc uint[16];
		Span<uint> t = stackalloc uint[16];
		for (int i = 0; i < 8; i++)
		{
			s[i] = cv[i];
		}
		s[8] = IV[0];
		s[9] = IV[1];
		s[10] = IV[2];
		s[11] = IV[3];
		s[12] = (uint)counter;
		s[13] = (uint)(counter >> 32);
		s[14] = blockLen;
		s[15] = flags;
		block.CopyTo(m);
		for (int r = 0; r < 7; r++)
		{
			G(s, 0, 4, 8, 12, m[0], m[1]);
			G(s, 1, 5, 9, 13, m[2], m[3]);
			G(s, 2, 6, 10, 14, m[4], m[5]);
			G(s, 3, 7, 11, 15, m[6], m[7]);
			G(s, 0, 5, 10, 15, m[8], m[9]);
			G(s, 1, 6, 11, 12, m[10], m[11]);
			G(s, 2, 7, 8, 13, m[12], m[13]);
			G(s, 3, 4, 9, 14, m[14], m[15]);
			if (r < 6)
			{
				for (int i2 = 0; i2 < 16; i2++)
				{
					t[i2] = m[Perm[i2]];
				}
				t.CopyTo(m);
			}
		}
		for (int j = 0; j < 8; j++)
		{
			outWords[j] = s[j] ^ s[j + 8];
			outWords[j + 8] = s[j + 8] ^ cv[j];
		}
	}

	private static void WordsFromBytes(ReadOnlySpan<byte> b, int len, Span<uint> w)
	{
		Span<byte> pad = stackalloc byte[64];
		pad.Clear();
		b.Slice(0, len).CopyTo(pad);
		for (int i = 0; i < 16; i++)
		{
			w[i] = BinaryPrimitives.ReadUInt32LittleEndian(pad.Slice(i * 4));
		}
	}

	private static uint[] ParentCv(uint[] left, ReadOnlySpan<uint> right, uint extraFlags, Span<uint> full)
	{
		Span<uint> block = stackalloc uint[16];
		left.CopyTo(block);
		right.CopyTo(block.Slice(8));
		Compress(IV, block, 0uL, 64u, 4u | extraFlags, full);
		uint[] r = new uint[8];
		full.Slice(0, 8).CopyTo(r);
		return r;
	}

	private void PushCv(uint[] cv, ulong totalChunks)
	{
		Span<uint> full = stackalloc uint[16];
		while ((totalChunks & 1) == 0L)
		{
			uint[] left = _stack[--_stackLen];
			cv = ParentCv(left, cv, 0u, full);
			totalChunks >>= 1;
		}
		_stack[_stackLen++] = cv;
	}

	private uint StartFlag => (_blocks == 0) ? 1u : 0u;

	public void Append(ReadOnlySpan<byte> data)
	{
		Span<uint> w = stackalloc uint[16];
		Span<uint> full = stackalloc uint[16];
		while (data.Length > 0)
		{
			if (_blocks * 64 + _bufLen == 1024)
			{
				WordsFromBytes(_buf, _bufLen, w);
				Compress(_cv, w, _chunkCounter, (uint)_bufLen, StartFlag | 2u, full);
				uint[] cvNew = new uint[8];
				full.Slice(0, 8).CopyTo(cvNew);
				PushCv(cvNew, _chunkCounter + 1);
				Array.Copy(IV, _cv, 8);
				_chunkCounter++;
				_blocks = 0;
				_bufLen = 0;
			}
			if (_bufLen == 64)
			{
				WordsFromBytes(_buf, 64, w);
				Compress(_cv, w, _chunkCounter, 64u, StartFlag, full);
				full.Slice(0, 8).CopyTo(_cv);
				_blocks++;
				_bufLen = 0;
			}
			int take = Math.Min(64 - _bufLen, data.Length);
			data.Slice(0, take).CopyTo(_buf.AsSpan(_bufLen));
			_bufLen += take;
			data = data.Slice(take);
		}
	}

	public byte[] Finish()
	{
		Span<uint> w = stackalloc uint[16];
		Span<uint> full = stackalloc uint[16];
		WordsFromBytes(_buf, _bufLen, w);
		uint[] inCv = (uint[])_cv.Clone();
		uint[] blockWords = w.ToArray();
		ulong counter = _chunkCounter;
		uint blockLen = (uint)_bufLen;
		uint flags = StartFlag | 2u;
		for (int i = _stackLen - 1; i >= 0; i--)
		{
			Compress(inCv, blockWords, counter, blockLen, flags, full);
			uint[] cv = new uint[8];
			full.Slice(0, 8).CopyTo(cv);
			Span<uint> pb = stackalloc uint[16];
			_stack[i].CopyTo(pb);
			cv.CopyTo(pb.Slice(8));
			inCv = (uint[])IV.Clone();
			blockWords = pb.ToArray();
			counter = 0uL;
			blockLen = 64u;
			flags = 4u;
		}
		Compress(inCv, blockWords, counter, blockLen, flags | 8u, full);
		byte[] result = new byte[32];
		for (int k = 0; k < 8; k++)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(k * 4), full[k]);
		}
		return result;
	}

	public static byte[] Hash(ReadOnlySpan<byte> data)
	{
		Blake3 b = new Blake3();
		b.Append(data);
		return b.Finish();
	}
}
