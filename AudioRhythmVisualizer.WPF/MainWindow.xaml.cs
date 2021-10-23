using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AudioRhythmVisualizer.Core.ViewModel;
using NAudio.Utils;
using NAudio.Wave;
using ScottPlot;
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
		private const int MAX_BEATS_LINE_COUNT = 12;

		private MainViewModel viewModel;

		private DispatcherTimer dispatcherTimerPlayback;
		private DispatcherTimer dispatcherTimerGraphUpdate;
		private DispatcherTimer dispatcherTimerSFX;
		private SignalPlotConst<double> audioSignalPlot;
		private VLine playbackLine;
		private VLine cursorMarkLine;
		private VLine[] beatsLines;
		private Text[] beatsTexts;

		private WaveOut mainWaveOut;
		private SoundPlayer soundPlayer;

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
			mainPlot.Configuration.DoubleClickBenchmark = true;
			mainPlot.Configuration.Quality = QualityMode.Low;
			mainPlot.Configuration.UseRenderQueue = true;
			mainPlot.Configuration.LockVerticalAxis = true;
			mainPlot.Configuration.MiddleClickDragZoom = false;
			mainPlot.Configuration.RightClickDragZoom = false;
			mainPlot.Plot.Legend(false);

			mainPlot.RightClicked -= mainPlot.DefaultRightClickEvent;
			mainPlot.RightClicked += OnRightClicked;

			mainPlot.MouseLeftButtonDown += MainPlotOnMouseLeftButtonDown;
			mainPlot.KeyDown += OnKeyDown;
		}

		private void SetupDispatcher()
		{
			dispatcherTimerPlayback = new DispatcherTimer(DispatcherPriority.Send);
			dispatcherTimerPlayback.Tick += UpdatePosition;
			dispatcherTimerPlayback.Interval = new TimeSpan(0, 0, 0, 0, 2);
			dispatcherTimerPlayback.Start();
			dispatcherTimerGraphUpdate = new DispatcherTimer(DispatcherPriority.Render);
			dispatcherTimerGraphUpdate.Tick += UpdateGraph;
			dispatcherTimerGraphUpdate.Interval = new TimeSpan(0, 0, 0, 0, 16);
			dispatcherTimerGraphUpdate.Start();
			dispatcherTimerSFX = new DispatcherTimer(DispatcherPriority.Send);
			dispatcherTimerSFX.Tick += UpdateBeat;
			dispatcherTimerSFX.Interval = new TimeSpan(0, 0, 0, 0, 2);
			dispatcherTimerSFX.Start();
		}

		private void SetupAudioPlayer()
		{
			soundPlayer = new SoundPlayer(ResourceUtility.GetSoftBeatFile());
			soundPlayer.Load();

			mainWaveOut = new WaveOut();
		}

		private void AudioDataReady()
		{
			mainPlot.Plot.Clear();
			cursorMarkLine = null;

			audioSignalPlot =
				mainPlot.Plot.AddSignalConst(viewModel.audioDataChannel0, viewModel.audioFormat.SampleRate, Color.DodgerBlue);
			mainPlot.Plot.XAxis.Grid(false);
			mainPlot.Plot.YAxis.Grid(false);
			mainPlot.Plot.XAxis.TickDensity(0.5d);
			mainPlot.Plot.Title($"SampleRate: {viewModel.audioFormat.SampleRate}, {viewModel.audioFormat.Channels} Channel");
			mainPlot.Plot.SetOuterViewLimits(0, viewModel.fileStream.TotalTime.TotalSeconds + 5, -1, 1);
			mainPlot.Plot.XLabel($"Time (seconds), total length={viewModel.audioTimeLength}s");

			playbackLine = mainPlot.Plot.AddVerticalLine(0, Color.Orange);
			startPosition = TimeSpan.Zero;

			mainPlot.Render();

			SetPlaybackPosition(0);
			mainWaveOut.Init(viewModel.fileStream);
			mainWaveOut.Play();
		}

		private void BeatDataReady(bool valid)
		{
			ClearBeatsLines();
			if (valid)
			{
				BeatDataReady_RefreshPlot();
				RecalculateNextBeatIndex();
			}

			if (mainWaveOut.PlaybackState != PlaybackState.Playing)
			{
				mainPlot.Refresh();
			}
		}

		private void BeatDataReady_RefreshPlot()
		{
			beatsLines = new VLine[viewModel.beatsData.Length];
			beatsTexts = new Text[viewModel.beatsData.Length];
			for (int i = 0; i < viewModel.beatsData.Length; i++)
			{
				beatsLines[i] = mainPlot.Plot.AddVerticalLine(viewModel.beatsData[i], Color.SeaGreen);
				beatsTexts[i] = mainPlot.Plot.AddText(i.ToString(), viewModel.beatsData[i], -0.5f, 10f, Color.Coral);
			}

			UpdateGraph_Scale();
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
			foreach (Text text in beatsTexts)
			{
				if (text != null)
				{
					mainPlot.Plot.Remove(text);
				}
			}
			beatsLines = null;
			beatsTexts = null;
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
						ClearCursorMark(this, null);
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
				cursorMarkLine = mainPlot.Plot.AddVerticalLine(mouseCoordinate, Color.DarkRed);
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
			UpdateGraph_Scale();
			
			if (mainWaveOut != null && mainWaveOut.PlaybackState == PlaybackState.Playing)
			{
				playbackLine.X = currentTimePosition;
				mainPlot.Refresh();
			}

			AxisLimits axisInfo = mainPlot.Plot.GetAxisLimits();
			debugLabel.Content = $"{axisInfo.XMin}, {axisInfo.XCenter}, {axisInfo.XMax}, {axisInfo.XSpan}";
		}

		private void UpdateGraph_Scale()
		{
			if (beatsLines == null || beatsLines.Length == 0)
			{
				return;
			}
			
			AxisLimits axisInfo = mainPlot.Plot.GetAxisLimits();
			int linePerBeatCount = (int)Math.Ceiling(axisInfo.XSpan / viewModel.beatInterval / MAX_BEATS_LINE_COUNT);

			int firstIndex = -1;
			for (int i = 0; i < beatsLines.Length; i++)
			{
				VLine line = beatsLines[i];
				Text text = beatsTexts[i];

				if (line.X < axisInfo.XMin || line.X > axisInfo.XMax)
				{
					line.IsVisible = false;
					text.IsVisible = false;
				}
				else
				{
					if (firstIndex < 0)
					{
						firstIndex = i;
					}
					if ((i - firstIndex) % linePerBeatCount == 0)
					{
						line.IsVisible = true;
						text.IsVisible = true;
					}
					else
					{
						line.IsVisible = false;
						text.IsVisible = false;
					}
				}

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
				if (beatSoundEnableCheckbox.IsChecked.GetValueOrDefault())
				{
					soundPlayer.Play();
				}
				nextBeatIndex++;
			}
		}

		private void ClearCursorMark(object sender, RoutedEventArgs e)
		{
			if (cursorMarkLine != null)
			{
				mainPlot.Plot.Remove(cursorMarkLine);
				cursorMarkLine = null;
			}
		}
	}
}