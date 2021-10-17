using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AudioRhythmVisualizer.Core.ViewModel;
using NAudio.CoreAudioApi;
using NAudio.Mixer;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ScottPlot.Control;
using ScottPlot.Plottable;
using Color = System.Drawing.Color;

namespace AudioRhythmVisualizer.WPF
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow: Window
	{
		private MainViewModel viewModel;

		private DispatcherTimer dispatcherTimerPlayback;
		private DispatcherTimer dispatcherTimerGraphUpdate;
		private DispatcherTimer dispatcherTimerSFX;
		private SignalPlot audioSignalPlot;
		private VLine playbackLine;
		private VLine cursorMarkLine;
		private VLine[] beatsLines;
		private Text[] beatsLineLabels;

		private WasapiOut mainWaveOut;
		private MixingSampleProvider mixWaveProvider;
		private BeepSampleProvider beepProvider;

		private TimeSpan startPosition;
		private double currentTimePosition;
		private int nextBeatIndex;

		public MainWindow()
		{
			InitializeComponent();
			viewModel = DataContext as MainViewModel;
			viewModel.audioDataReady += AudioDataReady;
			viewModel.beatDataReady += BeatDataReady;

			SetupPlot();
			SetupDispatcher();
			SetupAudioPlayer();
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			mainWaveOut.Dispose();
		}

		private void SetupPlot()
		{
			mainPlot.Configuration.Quality = QualityMode.Low;
			mainPlot.Configuration.UseRenderQueue = true;
			mainPlot.Configuration.LockVerticalAxis = true;

			mainPlot.RightClicked -= mainPlot.DefaultRightClickEvent;
			mainPlot.RightClicked += OnRightClicked;

			mainPlot.MouseLeftButtonDown += MainPlotOnMouseLeftButtonDown;
			mainPlot.KeyDown += OnKeyDown;
		}

		private void SetupDispatcher()
		{
			dispatcherTimerPlayback = new DispatcherTimer(DispatcherPriority.Send);
			dispatcherTimerGraphUpdate = new DispatcherTimer(DispatcherPriority.Render);
			dispatcherTimerSFX = new DispatcherTimer(DispatcherPriority.Send);
			dispatcherTimerPlayback.Tick += UpdatePosition;
			dispatcherTimerGraphUpdate.Tick += UpdateGraph;
			dispatcherTimerSFX.Tick += UpdateBeat;
			dispatcherTimerPlayback.Interval = new TimeSpan(0, 0, 0, 0, 2);
			dispatcherTimerPlayback.Start();
			dispatcherTimerGraphUpdate.Interval = new TimeSpan(0, 0, 0, 0, 16);
			dispatcherTimerGraphUpdate.Start();
			dispatcherTimerSFX.Interval = new TimeSpan(0, 0, 0, 0, 2);
			dispatcherTimerSFX.Start();
		}

		private void SetupAudioPlayer()
		{
			using (WaveFileReader beatSoftWaveStream = new(ResourceUtility.GetSoftBeatFile()))
			{
				ISampleProvider isp = beatSoftWaveStream.ToSampleProvider();
				float[] buffer = new float[beatSoftWaveStream.Length / (beatSoftWaveStream.WaveFormat.BitsPerSample / 8)];
				isp.Read(buffer, 0, buffer.Length);
				beepProvider = new BeepSampleProvider(buffer, beatSoftWaveStream.WaveFormat);
			}

			mixWaveProvider = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
			mixWaveProvider.AddMixerInput(beepProvider);

			mainWaveOut = new WasapiOut(AudioClientShareMode.Shared, 30);
			mainWaveOut.Init(mixWaveProvider);
		}

		private void AudioDataReady()
		{
			mainPlot.Plot.Clear();
			cursorMarkLine = null;

			audioSignalPlot =
				mainPlot.Plot.AddSignal(viewModel.audioDataChannel0, viewModel.audioFormat.SampleRate, Color.DodgerBlue);
			mainPlot.Plot.Title($"SampleRate: {viewModel.audioFormat.SampleRate}, {viewModel.audioFormat.Channels} Channel");
			mainPlot.Plot.SetOuterViewLimits(0, viewModel.fileStream.TotalTime.TotalSeconds + 5, -1, 1);
			mainPlot.Plot.XLabel($"Time (seconds), total length={viewModel.audioTimeLength}s");

			playbackLine = mainPlot.Plot.AddVerticalLine(0, Color.Orange);
			startPosition = TimeSpan.Zero;

			mainPlot.Render();

			SetPlaybackPosition(0);
			mixWaveProvider.AddMixerInput(viewModel.fileStream);
			mainWaveOut.Play();
		}

		private void BeatDataReady(bool valid)
		{
			ClearBeatsLines();
			if (valid)
			{
				beatsLines = new VLine[viewModel.beatsData.Length];
				beatsLineLabels = new Text[viewModel.beatsData.Length];
				for (int i = 0; i < viewModel.beatsData.Length; i++)
				{
					beatsLines[i] = mainPlot.Plot.AddVerticalLine(viewModel.beatsData[i], Color.SeaGreen);
					beatsLineLabels[i] =
						mainPlot.Plot.AddText(i.ToString(), viewModel.beatsData[i], -0.5f, 12f, Color.Chocolate);
				}
			}
			if (mainWaveOut.PlaybackState != PlaybackState.Playing)
			{
				mainPlot.Refresh();
			}
		}

		private void ClearBeatsLines()
		{
			if (beatsLines == null)
			{
				return;
			}
			foreach (VLine vLine in beatsLines)
			{
				if (vLine != null)
				{
					mainPlot.Plot.Remove(vLine);
				}
			}
			foreach (Text text in beatsLineLabels)
			{
				if (text != null)
				{
					mainPlot.Plot.Remove(text);
				}
			}
			beatsLines = null;
			beatsLineLabels = null;
		}

		private void SetPlaybackPosition(double timePositionSecond)
		{
			startPosition = TimeSpan.FromSeconds(timePositionSecond);
			viewModel.fileStream.CurrentTime = startPosition;
			currentTimePosition = timePositionSecond;
			RecalculateNextBeatIndex();
		}

		private void RecalculateNextBeatIndex()
		{
			if (viewModel.beatsData == null)
			{
				nextBeatIndex = 0;
				return;
			}

			for (int i = 0; i < viewModel.beatsData.Length; i++)
			{
				double b = viewModel.beatsData[i];
				if (b > currentTimePosition)
				{
					nextBeatIndex = i;
					break;
				}
			}
		}

		private void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Space)
			{
				if (mainWaveOut.PlaybackState == PlaybackState.Playing)
				{
					mainWaveOut.Pause();
					playbackLine.IsVisible = false;
					if (cursorMarkLine != null)
					{
						mainWaveOut.Stop();
					}
				}
				else
				{
					if (cursorMarkLine != null)
					{
						SetPlaybackPosition(cursorMarkLine.X);
					}
					mainWaveOut.Play();
					playbackLine.IsVisible = true;
				}
			}
		}

		private void MainPlotOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			Keyboard.Focus(mainPlot);
		}

		private void OnRightClicked(object sender, EventArgs e)
		{
			Keyboard.Focus(mainPlot);
			double mouseCoordinate = mainPlot.GetMouseCoordinates().x;
			if (cursorMarkLine == null)
			{
				cursorMarkLine = mainPlot.Plot.AddVerticalLine(mouseCoordinate);
			}
			else
			{
				cursorMarkLine.X = mouseCoordinate;
				mainPlot.Refresh();
			}
		}

		private void UpdatePosition(object sender, EventArgs e)
		{
			if (mainWaveOut != null && mainWaveOut.PlaybackState == PlaybackState.Playing)
			{
				currentTimePosition = (mainWaveOut.GetPositionTimeSpan() + startPosition).TotalSeconds;
			}
		}

		private void UpdateGraph(object sender, EventArgs e)
		{
			if (mainWaveOut != null && mainWaveOut.PlaybackState == PlaybackState.Playing)
			{
				playbackLine.X = currentTimePosition;
				mainPlot.Refresh();
			}
		}

		private void UpdateBeat(object sender, EventArgs e)
		{
			if (viewModel.beatsData == null ||
				nextBeatIndex >= viewModel.beatsData.Length ||
				mainWaveOut.PlaybackState != PlaybackState.Playing)
			{
				return;
			}

			if (currentTimePosition >= viewModel.beatsData[nextBeatIndex])
			{
				beepProvider.RequestBeep();
				nextBeatIndex++;
			}
		}
	}
}