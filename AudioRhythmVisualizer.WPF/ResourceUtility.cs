using System.IO;
using System.Reflection;

namespace AudioRhythmVisualizer.WPF
{
	public static class ResourceUtility
	{
		public static string GetSoftBeatFile()
		{
			string assemblyPath = Assembly.GetEntryAssembly().Location;

			DirectoryInfo di = Directory.GetParent(assemblyPath);
			return Path.Combine(di.FullName, "SoftBeat.wav");
		}

	}
}