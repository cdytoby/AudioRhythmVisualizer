using System.IO;
using NAudio.Wave;

namespace AudioRhythmVisualizer.WPF
{
	public class BeepStream: WaveStream
	{
		private byte[] sourceRaw;
		private MemoryStream sourceStream;
		private WaveFormat sourceFormat;
		private long sourceLength;
		private long position;

		private bool requestBeep;

		public BeepStream(byte[] _sourceRaw, WaveFormat _sourceFormat, long _sourceLength)
		{
			sourceRaw = _sourceRaw;
			sourceStream = new MemoryStream(_sourceRaw);
			sourceFormat = _sourceFormat;
			sourceLength = _sourceLength;
		}

		/// <summary>
		/// Return source stream's wave format
		/// </summary>
		public override WaveFormat WaveFormat
		{
			get { return sourceFormat; }
		}

		/// <summary>
		/// LoopStream simply returns
		/// </summary>
		public override long Length
		{
			get { return sourceLength * 3; }
		}

		/// <summary>
		/// LoopStream simply passes on positioning to source stream
		/// </summary>
		public override long Position
		{
			get { return position * 3; }
			set { position = value < position ? value : 0; }
		}

		public void RequestBeep()
		{
			requestBeep = true;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int totalBytesRead = 0;

			if (requestBeep)
			{
				while (totalBytesRead < count)
				{
					int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
					if (bytesRead == 0)
					{
						sourceStream.Position = 0;
						break;
					}
					totalBytesRead += bytesRead;
				}
			}
			while (totalBytesRead < count)
			{
				int i = offset + totalBytesRead;
				buffer[i] = 0;
				totalBytesRead++;
			}

			return totalBytesRead;
		}
	}
}