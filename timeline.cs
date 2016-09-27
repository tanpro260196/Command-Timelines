using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TerrariaApi.Server;
using TShockAPI;

namespace CommandTimelines
{
	public partial class Timeline
	{
		/// <summary>
		/// Fired when the timeline finishes running, either naturally or by being cancelled.
		/// </summary>
		public event EventHandler<TimelineFinishedEventArgs> Finished;

		protected CancellationTokenSource TokenSource { get; set; }

		public int Current { get; protected set; }

		public string Name { get; }

		public List<string> Parameters { get; }

		protected Queue<Func<Task>> Queue { get; }

		public string Raw { get; }

		public bool Running => Worker?.Status == TaskStatus.Running || Worker?.Status == TaskStatus.WaitingForActivation;

		public Task Worker { get; protected set; }

		internal Timeline(string name, string data)
		{
			Name = name;
			Raw = data;

			Queue = new Queue<Func<Task>>();
		}

		internal Timeline(string name, string data, IEnumerable<string> parameters) : this(name, data)
		{
			Parameters = new List<string>(parameters);
		}

		public async Task CleanUp()
		{
			// TODO: put clean up commands here, if any; store them on a clean up queue?
		}

		/// <summary>
		/// Processes given data and loads any actions in it on the queue. Handle all exceptions to look for errors.
		/// Note: This empties the queue beforehand.
		/// </summary>
		/// <param name="data">The line-separated list of actions to process.</param>
		/// <exception cref="CannotRunException">Thrown if a command cannot be run from the console.</exception>
		/// <exception cref="EmptyTimelineException">Thrown if a timeline has no commands to run.</exception>
		/// <exception cref="MissingParameterException">
		/// Thrown if a required parameter was not passed to the constructor.
		/// </exception>
		/// <exception cref="TimelineException">
		/// Thrown when something went wrong that doesn't fit a more specific exception.
		/// The exception message is usually self-explanatory.
		/// </exception>
		public async void Process(string data)
		{
			// Create a token source to use for this processing
			TokenSource = new CancellationTokenSource();

			using (var reader = new StringReader(data))
			{
				int count = 0;

				string line = null;

				#region Functions

				// We use this to keep our line count to properly issue errors
				Func<Task> nextLine = async () =>
				{
					line = await reader.ReadLineAsync();
					count++;
				};

				// This will properly remove comments from lines
				Func<bool> isComment = () =>
				{
					// If line is a comment, skip
					if (line.StartsWith("//"))
						return true;

					// Deal with in-line comments
					if (line.Contains("//"))
						line = line.Remove(line.IndexOf("//"));

					// Trim the line
					line = line.TrimEnd();
					return false;
				};

				#endregion

				await nextLine();

				// Handle starting comments?
				while (isComment())
					await nextLine();

				// The file is empty?
				if (line == null)
					throw new EmptyTimelineException(Name);

				// Verify required parameters
				if (line.StartsWith("#req "))
				{
					string[] args = line.Remove(0, 5).Split(new[] { ", " }, StringSplitOptions.None);
					if (Parameters?.Count < args.Length)
						throw new MissingParameterException(String.Join(" ", args.Select(s => $"<{s}>")));

					await nextLine();
				}

				while (line != null)
				{
					if (!isComment())
					{

						// If parameters were passed, attempt to format the line with them
						if (Parameters?.Count > 0)
							line = String.Format(line, Parameters);

						if (line.StartsWith("#wait "))
						{
							int interval;
							if (!Int32.TryParse(line.Remove(0, 6), out interval))
								throw new TimelineException("Invalid interval for #wait action.");

							// Add a delay to the action queue
							Queue.Enqueue(() => Task.Delay(interval, TokenSource.Token));
						}
						else
						{
							string specifier = line.StartsWith(Commands.Specifier)
								|| line.StartsWith(Commands.SilentSpecifier)
								? line[0].ToString() : "";

							bool silent = specifier == Commands.SilentSpecifier;
							string text = String.IsNullOrWhiteSpace(specifier) ? line : line.Substring(1);

							var args = ParseParameters(text);

							// Not exactly sure what would cause this to be empty, but we don't handle empty commands
							if (args.Count > 0)
							{
								string cmdName = args[0].ToLower();
								args.RemoveAt(0);

								IEnumerable<Command> commands = Commands.ChatCommands.FindAll(c => c.HasAlias(cmdName));

								if (!commands.Any())
								{
									// If no commands matched, check possible responses set by other commands
									Queue.Enqueue(() => Task.Run(() =>
									{
										if (TSPlayer.Server.AwaitingResponse.ContainsKey(cmdName))
										{
											Action<CommandArgs> call = TSPlayer.Server.AwaitingResponse[cmdName];
											TSPlayer.Server.AwaitingResponse.Remove(cmdName);
											call(new CommandArgs(text, TSPlayer.Server, args));
										}
									}, TokenSource.Token));

									await nextLine();
									continue;
								}

								foreach (Command cmd in commands)
								{
									if (!cmd.AllowServer)
										throw new CannotRunException(Name, count, cmdName);

									Queue.Enqueue(() => Task.Run(() =>
									{
										// We do this to prevent logging commands executed by the timeline
										new Command(cmd.Permissions, cmd.CommandDelegate, cmd.Names.ToArray())
										{
											AllowServer = cmd.AllowServer,
											DoLog = false,
											HelpDesc = cmd.HelpDesc,
											HelpText = cmd.HelpText,
										}.Run(text, silent, TSPlayer.Server, args);
									}, TokenSource.Token));
								}
							}
						}
					}

					await nextLine();
				}
			}
		}

		/// <summary>
		/// Starts this timeline instance. Make sure to handle all exceptions.
		/// </summary>
		/// <exception cref="CannotRunException">Thrown if a command cannot be run from the console.</exception>
		/// <exception cref="EmptyTimelineException">Thrown if a timeline has no commands to run.</exception>
		/// <exception cref="MissingParameterException">
		/// Thrown if a required parameter was not passed to the constructor.
		/// </exception>
		/// <exception cref="TimelineException">
		/// Thrown when something went wrong that doesn't fit a more specific exception.
		/// The exception message is usually self-explanatory.
		/// </exception>
		public Task Start()
		{
			return Task.Run(() =>
			{
				if (!Running)
				{
					// Process the raw data
					Process(Raw);

					// Begin the update loop
					Worker = Task.Run(Update, TokenSource.Token);
				}
			});
		}

		public async Task Stop()
		{
			if (Running)
			{
				// This will cancel any Task currently running
				TokenSource.Cancel();

				// Run clean up commands
				await CleanUp();

				// Signal the finished event as having ended by cancellation
				Finished?.Invoke(this, new TimelineFinishedEventArgs(this, true));
			}
		}

		protected virtual async Task Update()
		{
			while (Queue.Count > 0)
			{
				Current++;
				await Queue.Dequeue()().LogExceptions().ConfigureAwait(false);
			}

			// Run clean up commands
			await CleanUp();

			Finished?.Invoke(this, new TimelineFinishedEventArgs(this));
		}

		public override string ToString() => Raw;
	}

	public class CannotRunException : TimelineException
	{
		public string Command { get; }

		public CannotRunException(string name, int line, string command) : base(name, line)
		{
			Command = command;
		}
	}

	/// <summary>
	/// Thrown when the timeline is an empty file.
	/// The exception message will be the file name.
	/// </summary>
	public class EmptyTimelineException : TimelineException
	{
		public EmptyTimelineException(string filename) : base(filename) { }

		public EmptyTimelineException(string filename, Exception inner) : base(filename, inner) { }
	}

	/// <summary>
	/// Thrown when not all required parameters were passed.
	/// The exception message will be formatted list of required parameters.
	/// </summary>
	public class MissingParameterException : TimelineException
	{
		public MissingParameterException(string formattedParameters) : base(formattedParameters) { }

		public MissingParameterException(string formattedParameters, Exception inner) : base(formattedParameters, inner) { }
	}

	public class TimelineException : Exception
	{
		public string Name { get; }

		public int Line { get; }

		public TimelineException() { }
		public TimelineException(string message) : base(message) { }
		public TimelineException(string message, Exception inner) : base(message, inner) { }

		public TimelineException(string name, int line) : base($"Timeline {name} had an error at line {line}.")
		{
			Name = name;
			Line = line;
		}
	}
}
