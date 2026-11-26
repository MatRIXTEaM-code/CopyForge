using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CopyForge.Engine;

/// <summary>A checksum of up to 256 bits, compared as a value.</summary>
internal readonly record struct HashKey(ulong A, ulong B, ulong C, ulong D)
{
	public static HashKey Once(VerifyAlgo algo, ReadOnlySpan<byte> data)
	{
		switch (algo)
		{
		case VerifyAlgo.XxHash128:
		{
			UInt128 v = XxHash128.HashToUInt128(data, 0L);
			return new HashKey((ulong)v, (ulong)(v >> 64), 0uL, 0uL);
		}
		case VerifyAlgo.Sha256:
		{
			Span<byte> d = stackalloc byte[32];
			SHA256.HashData(data, d);
			return FromDigest(d);
		}
		case VerifyAlgo.Blake3:
			return FromDigest(Blake3.Hash(data));
		default:
			return new HashKey(XxHash3.HashToUInt64(data, 0L), 0uL, 0uL, 0uL);
		}
	}

	internal static HashKey FromDigest(ReadOnlySpan<byte> d)
	{
		ulong a = MemoryMarshal.Read<ulong>(d);
		ulong b = (d.Length >= 16) ? MemoryMarshal.Read<ulong>(d.Slice(8)) : 0uL;
		ulong c = (d.Length >= 24) ? MemoryMarshal.Read<ulong>(d.Slice(16)) : 0uL;
		ulong e = (d.Length >= 32) ? MemoryMarshal.Read<ulong>(d.Slice(24)) : 0uL;
		return new HashKey(a, b, c, e);
	}
}

/// <summary>Incremental hasher for whole-file verification.</summary>
internal sealed class HashAcc : IDisposable
{
	private readonly VerifyAlgo _algo;

	private readonly XxHash3? _x3;

	private readonly XxHash128? _x128;

	private readonly IncrementalHash? _sha;

	private readonly Blake3? _b3;

	public HashAcc(VerifyAlgo algo)
	{
		_algo = algo;
		switch (algo)
		{
		case VerifyAlgo.XxHash128:
			_x128 = new XxHash128();
			break;
		case VerifyAlgo.Sha256:
			_sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			break;
		case VerifyAlgo.Blake3:
			_b3 = new Blake3();
			break;
		default:
			_x3 = new XxHash3();
			break;
		}
	}

	public void Append(ReadOnlySpan<byte> data)
	{
		if (_x128 != null)
		{
			_x128.Append(data);
		}
		else if (_sha != null)
		{
			_sha.AppendData(data);
		}
		else if (_b3 != null)
		{
			_b3.Append(data);
		}
		else
		{
			_x3!.Append(data);
		}
	}

	public HashKey Finish()
	{
		if (_x128 != null)
		{
			UInt128 v = _x128.GetCurrentHashAsUInt128();
			return new HashKey((ulong)v, (ulong)(v >> 64), 0uL, 0uL);
		}
		if (_sha != null)
		{
			return HashKey.FromDigest(_sha.GetCurrentHash());
		}
		if (_b3 != null)
		{
			return HashKey.FromDigest(_b3.Finish());
		}
		return new HashKey(_x3!.GetCurrentHashAsUInt64(), 0uL, 0uL, 0uL);
	}

	public void Dispose()
	{
		_sha?.Dispose();
	}
}
