using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using TShockAPI;
using System.IO;

namespace CmdTimelines
{
	public class Timeline
	{
		public string file { get; set; }
		public Timer wait = new Timer();
		public TSPlayer player { get; set; }
		public StreamReader tl { get; set; }
		public bool going { get; set; }
		uint lineNumber { get; set; }
		List<string> arguments = new List<string>();
		bool sentMessage { get; set; }

		public Timeline()
		{
			wait.AutoReset = false;
			wait.Elapsed += waitTimer;
		}

		public void Start()
		{
			going = true;
			string line;
			bool canClose = true;
			bool silent = false;
			while ((line = tl.ReadLine()) != null)
			{
				lineNumber++;
				if (arguments.Count > 0)
					line = String.Format(line, arguments.ToArray());

				if (line.StartsWith(Commands.SilentSpecifier))
				{
					silent = true;
					line = line.Remove(0, Commands.SilentSpecifier.Length);
				}

				if (!line.StartsWith("//"))
				{
					if (line.Contains("//"))
					{
						line = line.Remove(line.IndexOf("//"));
						while (line.EndsWith(" "))
							line = line.Remove(line.Length - 1);
					}
					if (line.StartsWith("#wait "))
					{
						canClose = false;
						double intervl = 1000;
						if (!Double.TryParse(line.Remove(0, 6), out intervl))
							Console.WriteLine("Timeline {0} had an error at line {1}.", file, lineNumber);
						wait.Interval = intervl;
						wait.Start();
						break;
					}
					else if (line.StartsWith("#req "))
					{
						string[] args = line.Remove(0, 5).Split(new string[] { ", " }, StringSplitOptions.None);
						if (arguments.Count < args.Length)
						{
							StringBuilder rgs = new StringBuilder();
							foreach (string str in args)
							{
								if (rgs.ToString() != "")
									rgs.Append(" ");
								rgs.Append("<");
								rgs.Append(str);
								rgs.Append(">");
							}
							player.SendErrorMessage(String.Format("Incorrect arguments! Correct syntax: /timeline start {0} {1}", file, rgs.ToString()));
							break;
						}
					}
					else
					{
						try
						{
							Commands.HandleCommand(player, String.Concat(silent ? Commands.SilentSpecifier : Commands.Specifier, line));
						}
						catch
						{
							Console.WriteLine("Timeline {0} had an error at line {1}.", file, lineNumber);
						}
					}
				}
				if (!sentMessage)
				{
					player.SendSuccessMessage(String.Format("{0} has started.", file));
					sentMessage = true;
				}
			}
			if (canClose)
			{
				tl.Close();
				going = false;
				wait.Stop();
			}
		}

		public void Initialize(CommandArgs e)
		{
			tl = new StreamReader(Path.Combine(TShock.SavePath, file));
			player = e.Player;
			arguments.Clear();
			for (int i = 2; i < e.Parameters.Count; i++)
				arguments.Add(e.Parameters[i]);
			lineNumber = 0;
			sentMessage = false;
			Start();
		}

		private void waitTimer(object source, ElapsedEventArgs e)
		{
			Start();
		}
	}
}
