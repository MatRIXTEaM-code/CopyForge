namespace CopyForge.Engine;

internal readonly record struct VolInfo(bool Network, bool Removable, bool Solid, bool SolidKnown, string Fs)
{
	public string Describe()
	{
		if (!Network)
		{
			if (!Removable)
			{
				if (!SolidKnown)
				{
					return "local";
				}
				if (!Solid)
				{
					return "HDD";
				}
				return "SSD";
			}
			return "removable";
		}
		return "network";
	}
}
