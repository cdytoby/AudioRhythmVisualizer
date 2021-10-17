using System.IO;
using System.Linq;
using NAudio.Wave;

namespace AudioRhythmVisualizer.WPF
{
	public class BeepSampleProvider: ISampleProvider
	{
		private readonly float[] sourceSampleRaw;
		private readonly long sourceLength;

		private bool requestBeep;
		private bool isBeeping;
		private long position;

		public WaveFormat WaveFormat { get; }

		public BeepSampleProvider(float[] _sourceSampleRaw, WaveFormat _sourceFormat)
		{
			sourceSampleRaw = _sourceSampleRaw;
			sourceLength = _sourceSampleRaw.Length;
			WaveFormat = _sourceFormat;
		}

		public void RequestBeep()
		{
			if (!isBeeping)
			{
				requestBeep = true;
			}
		}

		public int Read(float[] buffer, int offset, int count)
		{
			int totalBytesRead = 0;

			if (requestBeep)
			{
				isBeeping = true;
				requestBeep = false;
				position = 0;
			}

			if (isBeeping)
			{
				for (int i = 0; i < count; i++)
				{
					if (position + i < sourceLength)
					{
						buffer[i + offset] = sourceSampleRaw[i + position];
					}
					else
					{
						buffer[i + offset] = 0;
						isBeeping = false;
					}

					totalBytesRead++;
				}

				position += count;
			}
			else
			{
				for (int i = 0; i < count; i++)
				{
					buffer[i + offset] = 0;
					totalBytesRead++;
				}
			}

			return totalBytesRead;
		}

	}
}