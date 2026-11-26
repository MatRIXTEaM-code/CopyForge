using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CopyForge.Engine;

public sealed class PathFilter
{
	private sealed record Rule(Regex Rx, bool IsPath, bool DirOnly);

	private readonly List<Rule> _include;

	private readonly List<Rule> _exclude;

	private readonly long _min;

	private readonly long _max;

	private readonly DateTime? _since;

	public PathFilter(JobOptions o)
	{
		_include = Parse(o.Include);
		_exclude = Parse(o.Exclude);
		_min = o.MinSizeBytes;
		_max = o.MaxSizeBytes;
		_since = o.ModifiedSinceUtc;
	}

	private static List<Rule> Parse(string? text)
	{
		List<Rule> list = new List<Rule>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return list;
		}
		string[] array = text.Split(new char[3] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Replace('\\', '/');
			bool dirOnly = text2.EndsWith('/');
			text2 = text2.Trim('/');
			if (text2.Length != 0)
			{
				bool flag = text2.Contains('/');
				string text3 = Regex.Escape(text2).Replace("\\*", ".*").Replace("\\?", ".");
				string pattern = (flag ? ("(^|/)" + text3 + "$") : ("^" + text3 + "$"));
				list.Add(new Rule(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant), flag, dirOnly));
			}
		}
		return list;
	}

	private static bool Hit(Rule r, string rel, string name)
	{
		return r.Rx.IsMatch(r.IsPath ? rel.Replace('\\', '/') : name);
	}

	public bool DirExcluded(string rel, string name)
	{
		foreach (Rule item in _exclude)
		{
			if (Hit(item, rel, name))
			{
				return true;
			}
		}
		return false;
	}

	public bool FileAccepted(string rel, string name, long size, DateTime mtimeUtc)
	{
		foreach (Rule item in _exclude)
		{
			if (!item.DirOnly && Hit(item, rel, name))
			{
				return false;
			}
		}
		bool flag = false;
		bool flag2 = false;
		foreach (Rule item2 in _include)
		{
			if (!item2.DirOnly)
			{
				flag = true;
				if (Hit(item2, rel, name))
				{
					flag2 = true;
					break;
				}
			}
		}
		if (flag && !flag2)
		{
			return false;
		}
		if (size < _min)
		{
			return false;
		}
		if (_max > 0 && size > _max)
		{
			return false;
		}
		if (_since.HasValue && mtimeUtc < _since.Value)
		{
			return false;
		}
		return true;
	}
}
