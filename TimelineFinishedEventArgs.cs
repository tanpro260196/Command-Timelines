using System;

namespace CommandTimelines
{
	public class TimelineFinishedEventArgs : EventArgs
	{
		/// <summary>
		/// Whether or not the timeline execution was cancelled.
		/// </summary>
		public bool Cancelled { get; }

		public Timeline Timeline { get; }

		public TimelineFinishedEventArgs(Timeline timeline, bool cancelled = false)
		{
			Cancelled = cancelled;

			Timeline = timeline;
		}
	}
}
