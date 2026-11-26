using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CopyForge.Engine.Net;

public static class Wire
{
	public const int MaxFrame = 8388608;

	public const int ChunkSize = 1048576;

	public const int MaxPath = 1024;

	public const int MaxBatch = 4000;

	public static readonly byte[] Magic = Encoding.ASCII.GetBytes("CFG2");

	/// <summary>Per-connection AES-256-GCM state. Keys come from the pre-shared secret (HKDF), so the data stays confidential
	/// even if the TLS key exchange is broken later (for example by a quantum computer recording traffic today).</summary>
	private sealed class FramePair
	{
		public AesGcm Tx = null!;

		public AesGcm Rx = null!;

		public ulong TxCtr;

		public ulong RxCtr;
	}

	private const int TagSize = 16;

	private static readonly ConditionalWeakTable<Stream, FramePair> Pairs = new ConditionalWeakTable<Stream, FramePair>();

	/// <summary>Turn on frame encryption for this stream. Both ends call this right after the login succeeds.</summary>
	public static void EnableEncryption(Stream s, byte[] psk, byte[] nonce, byte[] certHash, bool isClient)
	{
		byte[] salt = new byte[nonce.Length + certHash.Length];
		nonce.CopyTo(salt, 0);
		certHash.CopyTo(salt, nonce.Length);
		byte[] c2s = HKDF.DeriveKey(HashAlgorithmName.SHA256, psk, 32, salt, Encoding.ASCII.GetBytes("cfg-enc-v1-c2s"));
		byte[] s2c = HKDF.DeriveKey(HashAlgorithmName.SHA256, psk, 32, salt, Encoding.ASCII.GetBytes("cfg-enc-v1-s2c"));
		FramePair p = new FramePair
		{
			Tx = new AesGcm(isClient ? c2s : s2c, TagSize),
			Rx = new AesGcm(isClient ? s2c : c2s, TagSize)
		};
		Pairs.AddOrUpdate(s, p);
		CryptographicOperations.ZeroMemory(c2s);
		CryptographicOperations.ZeroMemory(s2c);
	}

	public static bool IsEncrypted(Stream s)
	{
		return Pairs.TryGetValue(s, out _);
	}

	private static void MakeNonce(Span<byte> nonce, ulong ctr)
	{
		nonce.Clear();
		BinaryPrimitives.WriteUInt64BigEndian(nonce.Slice(4), ctr);
	}

	public static async Task WriteFrameAsync(Stream s, byte type, ReadOnlyMemory<byte> payload, CancellationToken ct)
	{
		if (Pairs.TryGetValue(s, out FramePair? pair))
		{
			byte[] frame = new byte[5 + payload.Length + TagSize];
			frame[0] = type;
			BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), payload.Length + TagSize);
			Span<byte> nonce = stackalloc byte[12];
			MakeNonce(nonce, pair.TxCtr++);
			pair.Tx.Encrypt(nonce, payload.Span, frame.AsSpan(5, payload.Length), frame.AsSpan(5 + payload.Length, TagSize), new byte[1] { type });
			await s.WriteAsync(frame, ct).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		byte[] array = new byte[5] { type, 0, 0, 0, 0 };
		BinaryPrimitives.WriteInt32BigEndian(array.AsSpan(1), payload.Length);
		await s.WriteAsync(array, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (payload.Length > 0)
		{
			await s.WriteAsync(payload, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public static async Task<(byte type, byte[] payload)> ReadFrameAsync(Stream s, CancellationToken ct)
	{
		byte[] head = new byte[5];
		await ReadExactAsync(s, head, ct).ConfigureAwait(continueOnCapturedContext: false);
		int num = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(1));
		if (num < 0 || num > 8388608)
		{
			throw new InvalidDataException("frame too large");
		}
		byte[] p = new byte[num];
		if (num > 0)
		{
			await ReadExactAsync(s, p, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (Pairs.TryGetValue(s, out FramePair? pair))
		{
			if (num < TagSize)
			{
				throw new InvalidDataException("frame too short");
			}
			byte[] plain = new byte[num - TagSize];
			Span<byte> nonce = stackalloc byte[12];
			MakeNonce(nonce, pair.RxCtr++);
			try
			{
				pair.Rx.Decrypt(nonce, p.AsSpan(0, plain.Length), p.AsSpan(plain.Length, TagSize), plain, new byte[1] { head[0] });
			}
			catch (CryptographicException)
			{
				throw new InvalidDataException("frame failed authentication (tampered, replayed or out of order)");
			}
			return (type: head[0], payload: plain);
		}
		return (type: head[0], payload: p);
	}

	public static async Task ReadExactAsync(Stream s, Memory<byte> buf, CancellationToken ct)
	{
		int num;
		for (int got = 0; got < buf.Length; got += num)
		{
			num = await s.ReadAsync(buf.Slice(got), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (num <= 0)
			{
				throw new EndOfStreamException();
			}
		}
	}

	public static byte[] Mac(byte[] psk, byte[] nonce, byte[] certHash)
	{
		using HMACSHA256 hMACSHA = new HMACSHA256(psk);
		hMACSHA.TransformBlock(Encoding.ASCII.GetBytes("cfg-auth-v1"), 0, 11, null, 0);
		hMACSHA.TransformBlock(nonce, 0, nonce.Length, null, 0);
		hMACSHA.TransformFinalBlock(certHash, 0, certHash.Length);
		return hMACSHA.Hash;
	}

	public static string? SafeRel(string rel)
	{
		if (string.IsNullOrEmpty(rel) || rel.Length > 1024)
		{
			return null;
		}
		if (rel.Contains('\0') || rel.Contains('\\') || rel.StartsWith('/') || rel.Contains(':'))
		{
			return null;
		}
		string[] array = rel.Split('/');
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.Length == 0 || text == "." || text == ".." || text.Length > 255)
			{
				return null;
			}
			if (text.EndsWith(' ') || text.EndsWith('.'))
			{
				return null;
			}
			if (text.IndexOfAny(new char[6] { '<', '>', '"', '|', '?', '*' }) >= 0)
			{
				return null;
			}
			string text2 = text;
			for (int j = 0; j < text2.Length; j++)
			{
				if (text2[j] < ' ')
				{
					return null;
				}
			}
			string text3 = text.Split('.')[0].ToUpperInvariant();
			bool flag;
			switch (text3)
			{
			case "CON":
			case "PRN":
			case "AUX":
			case "NUL":
			case "CLOCK$":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag || (text3.Length == 4 && (text3.StartsWith("COM") || text3.StartsWith("LPT")) && char.IsDigit(text3[3])))
			{
				return null;
			}
			if (text.EndsWith(".cfpart", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
		}
		return string.Join('/', array);
	}

	public static string Hex(byte[] b)
	{
		return Convert.ToHexString(b).ToLowerInvariant();
	}

	public static byte[] LoadOrCreatePsk(string path)
	{
		if (File.Exists(path))
		{
			byte[] array = Convert.FromBase64String(File.ReadAllText(path).Trim());
			if (array.Length < 16)
			{
				throw new InvalidDataException("pre-shared secret is too short (need at least 16 bytes)");
			}
			return array;
		}
		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(path, Convert.ToBase64String(bytes));
		PrivateFile.Restrict(path);
		return bytes;
	}
}
