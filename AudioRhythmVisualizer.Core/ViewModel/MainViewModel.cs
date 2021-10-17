using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.Input;
using NAudio.Wave;
using PropertyChanged;

namespace AudioRhythmVisualizer.Core.ViewModel
{
	public class MainViewModel: INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		public event Action audioDataReady;
		public event Action<bool> beatDataReady;

		public string filePath { get; set; }
		public WaveStream fileStream { get; private set; }
		public ISampleProvider sampleProvider { get; private set; }
		public WaveFormat audioFormat { get; private set; }
		public double[] audioDataChannel0 { get; set; }

		[OnChangedMethod(nameof(GenerateBeatsData))]
		public float bpm { get; set; }
		[OnChangedMethod(nameof(GenerateBeatsData))]
		public float bpmAlignOffset { get; set; }
		public float bpmScrollStep { get; set; } = 0.1f;
		public double[] beatsData { get; private set; }

		public double audioTimeLength => fileStream?.TotalTime.TotalSeconds ?? 0;

		public ICommand loadCommand { get; private set; }

		public MainViewModel()
		{
			loadCommand = new RelayCommand(LoadFile);
			filePath = @"H:\Projects\AudioRhythmVisual\Original.wav";
			//1.2, around 160
		}

		[SuppressMessage("ReSharper.DPA", "DPA0001: Memory allocation issues")]
		private void LoadFile()
		{
			if (!File.Exists(filePath))
			{
				return;
			}

			if (fileStream != null)
			{
				fileStream.Close();
				fileStream.Dispose();
			}

			fileStream = new WaveFileReader(filePath);
			sampleProvider = fileStream.ToSampleProvider();
			audioFormat = sampleProvider.WaveFormat;

			int sampleCount = (int)(fileStream.Length / audioFormat.BitsPerSample / 8);
			int channelCount = audioFormat.Channels;
			List<double> audio = new(sampleCount);
			float[] buffer = new float[audioFormat.SampleRate * channelCount];
			int samplesRead = 0;

			while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
			{
				for (int i = 0; i < buffer.Length; i++)
				{
					if (i % channelCount == 0)
					{
						audio.Add(buffer[i]);
					}
				}
			}
			audioDataChannel0 = audio.ToArray();

			audioDataReady?.Invoke();

			GenerateBeatsData();
		}

		private void GenerateBeatsData()
		{
			if (audioTimeLength == 0 || bpm == 0)
			{
				beatsData = null;
				beatDataReady?.Invoke(false);
			}
			else
			{
				double interval = 1 / (bpm / 60);
				int beatCount = (int)Math.Floor((audioTimeLength - bpmAlignOffset) / interval);
				beatsData = new double[beatCount];
				for (int i = 0; i < beatCount; i++)
				{
					beatsData[i] = bpmAlignOffset + interval * i;
				}
				beatDataReady?.Invoke(true);
			}
		}
	}
}