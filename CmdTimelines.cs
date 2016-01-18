using System;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using System.IO;

namespace CmdTimelines
{
	[ApiVersion(1, 22)]
	public class CmdTimelines : TerrariaPlugin
	{
		List<Timeline> running = new List<Timeline>();

		public override string Author
		{
			get { return "GameRoom & Enerdy"; }
		}

		public override string Description
		{
			get { return "Reads command macros."; }
		}

		public override string Name
		{
			get { return "Command Timelines"; }
		}

		public override Version Version
		{
			get { return new Version(1, 1); }
		}

		public CmdTimelines(Main game) : base(game)
		{
		}

		public override void Initialize()
		{
			Commands.ChatCommands.Add(new Command("", TimelineDo, "timeline", "tl")
			{
				HelpText = "Commands: /timeline start <file> [arguments], /timeline stop <file>, /timeline show"
			});
		}

		void TimelineDo(CommandArgs e)
		{
			string text = "Invalid syntax! Commands: /timeline start <file> [arguments], /timeline stop <file>, /timeline show";
			if (e.Parameters.Count < 1)
				e.Player.SendErrorMessage(text);
			else if (e.Parameters[0] != "show" && e.Parameters.Count >= 2
				&& !e.Player.HasPermission("timeline.admin.useall")
				&& !e.Player.HasPermission(String.Concat("timeline.use-", e.Parameters[1]))
				&& !e.Player.HasPermission(String.Concat("timeline.use-", Path.GetDirectoryName(e.Parameters[1])))
				&& !e.Player.HasPermission(String.Concat("timeline.use-", Path.GetDirectoryName(e.Parameters[1])).Replace("\\", "/")))
				e.Player.SendErrorMessage("You do not have access to this command.");
			else
			{
				string filename = "";
				if (e.Parameters.Count >= 2)
					filename = e.Parameters[1];

				switch (e.Parameters[0])
				{
					case "start":
						if (!File.Exists(Path.Combine(TShock.SavePath, filename)))
							e.Player.SendErrorMessage(String.Format("{0} doesn't exist!", filename));
						else if (running.Exists(x => x.file == filename) && running.Find(x => x.file == filename).going)
							e.Player.SendErrorMessage(String.Format("{0} is already running.", filename));
						else
						{
							Timeline newTL;
							if (running.Exists(x => x.file == filename))
							{
								newTL = running.Find(x => x.file == filename);
							}
							else
							{
								newTL = new Timeline { file = filename };
								running.Add(newTL);
							}
							newTL.Initialize(e);
						}
						break;

					case "stop":
						if (!running.Exists(x => x.file == filename) || !running.Find(x => x.file == filename).going)
							e.Player.SendErrorMessage(String.Format("{0} isn't running.", filename));
						else
						{
							var instance = running.Find(x => x.file == filename);
							instance.going = false;
							instance.tl.Close();
							instance.wait.Stop();
							e.Player.SendSuccessMessage(String.Format("{0} was stopped.", filename));
						}
						break;

					case "show":
						string show = String.Join(", ", running.FindAll(t => t.going).Select(t => t.file));
						if (String.IsNullOrEmpty(show))
							e.Player.SendInfoMessage("No timelines are currently running.");
						else
							e.Player.SendInfoMessage(String.Concat("Currently running timelines: ", show));
						break;

					default:
						e.Player.SendErrorMessage(text);
						break;
				}
			}
		}
	}
}