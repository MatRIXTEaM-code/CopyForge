using System;

namespace CopyForge.Engine;

public sealed class AutoTuner
{
	private readonly int _min;

	private readonly int _max;

	private int _limit;

	private int _dir = 1;

	private double _lastScore = -1.0;

	private int _settled;

	private int _cooldown;

	public int Limit => _limit;

	public AutoTuner(int start, int min, int max)
	{
		_min = Math.Max(1, min);
		_max = Math.Max(_min, max);
		_limit = Math.Clamp(start, _min, _max);
	}

	public static double Score(long bytes, long files)
	{
		return (double)bytes + (double)files * 256.0 * 1024.0;
	}

	public int Next(double scorePerSec)
	{
		if (_lastScore < 0.0)
		{
			_lastScore = scorePerSec;
			Step();
			return _limit;
		}
		if (_cooldown > 0)
		{
			_cooldown--;
			_lastScore = scorePerSec;
			return _limit;
		}
		double num = ((_lastScore <= 0.0) ? 0.0 : ((scorePerSec - _lastScore) / _lastScore));
		_lastScore = scorePerSec;
		if (num > 0.06)
		{
			_settled = 0;
			Step();
		}
		else if (num < -0.1)
		{
			_settled = 0;
			_dir = -_dir;
			Step();
			_cooldown = 2;
		}
		else if (++_settled >= 4)
		{
			_settled = 0;
			_dir = 1;
			Step();
		}
		return _limit;
	}

	private void Step()
	{
		int num = Math.Clamp(_limit + _dir, _min, _max);
		if (num == _limit)
		{
			_dir = -_dir;
		}
		_limit = num;
	}
}
