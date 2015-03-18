using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using System.IO;
using System.Timers;

namespace CmdTimelines {
    [ApiVersion(1, 17)]
    public class CmdTimelines : TerrariaPlugin {
        List<Timeline> running = new List<Timeline>();
        public CmdTimelines(Main game) : base(game) {
        }
        public override void Initialize() {
            Commands.ChatCommands.Add(new Command("", TimelineDo, "timeline", "tl") {
                HelpText = "Commands: /timeline start <file> [arguments], /timeline stop <file>, /timeline show"
            });
        }
        public override Version Version {
            get { return new Version("1.0.3"); }
        }
        public override string Name {
            get { return "Command Timelines"; }
        }
        public override string Author {
            get { return "GameRoom"; }
        }
        public override string Description {
            get { return "Reads command macros."; }
        }

        void TimelineDo(CommandArgs e) {
            string text = "Invalid command! Commands: /timeline start <file> [arguments], /timeline stop <file>, /timeline show";
            if (e.Parameters.Count < 1) e.Player.SendErrorMessage(text);
            else if (e.Parameters[0] != "show" && e.Parameters.Count >= 2 && !e.Player.Group.HasPermission("timeline.admin.useall") && !e.Player.Group.HasPermission(String.Concat("timeline.use-", e.Parameters[1])) && !e.Player.Group.HasPermission(String.Concat("timeline.use-", Path.GetDirectoryName(e.Parameters[1]))) && !e.Player.Group.HasPermission(String.Concat("timeline.use-", Path.GetDirectoryName(e.Parameters[1])).Replace("\\", "/")))
                e.Player.SendErrorMessage("You don't have permission to use that command.");
            else {
                string filename;
                if (e.Parameters.Count >= 2) filename = e.Parameters[1];
                else filename = "";
                switch (e.Parameters[0]) {
                    case "start":
                        if (!File.Exists(Path.Combine(TShock.SavePath, filename))) e.Player.SendErrorMessage(String.Format("{0} doesn't exist!", filename));
                        else if (running.Exists(x => x.file == filename) && running.Find(x => x.file == filename).going) e.Player.SendErrorMessage(String.Format("{0} is already running.", filename));
                        else {
                            Timeline newTL;
                            if (running.Exists(x => x.file == filename)) {
                                newTL = running.Find(x => x.file == filename);
                            } else {
                                newTL = new Timeline { file = filename };
                                newTL.Initialize2();
                                running.Add(newTL);
                            }
                            newTL.Initialize(e);
                        }
                        break;

                    case "stop":
                        if (!running.Exists(x => x.file == filename) || !running.Find(x => x.file == filename).going) e.Player.SendErrorMessage(String.Format("{0} isn't running.", filename));
                        else {
                            var instance = running.Find(x => x.file == filename);
                            instance.going = false;
                            instance.tl.Close();
                            instance.wait.Stop();
                            e.Player.SendSuccessMessage(String.Format("{0} was stopped.", filename));
                        }
                        break;

                    case "show":
                        StringBuilder sb = new StringBuilder();
                        foreach (Timeline TL in running)
                            if (TL.going) {
                                if (sb.ToString() != "") sb.Append(", ");
                                sb.Append(TL.file);
                            }
                        if (sb.ToString() == "") e.Player.SendInfoMessage("No timelines are currently running.");
                        else e.Player.SendInfoMessage(String.Concat("Currently running timelines: ", sb.ToString()));
                        break;

                    default:
                        e.Player.SendErrorMessage(text);
                        break;
                }
            }
        }
    }

    public class Timeline {
        public string file { get; set; }
        public Timer wait = new Timer();
        public TSPlayer player { get; set; }
        public StreamReader tl { get; set; }
        public bool going { get; set; }
        uint lineNumber { get; set; }
        List<string> arguments = new List<string>();
        bool sentMessage { get; set; }
        public void Start() {
            going = true;
            string line;
            bool canClose = true;
            bool silent = false;
            while ((line = tl.ReadLine()) != null) {
                lineNumber++;
                if(arguments.Count > 0) line = String.Format(line, arguments.ToArray());
                if (line.StartsWith(Commands.SilentSpecifier)) {
                    silent = true;
                    line = line.Remove(0, Commands.SilentSpecifier.Length);
                }
                if (!line.StartsWith("//")) {
                    if (line.Contains("//")) {
                        line = line.Remove(line.IndexOf("//"));
                        while (line.EndsWith(" ")) line = line.Remove(line.Length - 1);
                    }
                    if (line.StartsWith("#wait ")) {
                        canClose = false;
                        double intervl = 1000;
                        if (!double.TryParse(line.Remove(0, 6), out intervl))
                            Console.WriteLine("Timeline {0} had an error at line {1}.", file, lineNumber);
                        wait.Interval = intervl;
                        wait.Start();
                        break;
                    } else if (line.StartsWith("#req ")) {
                        string[] args = line.Remove(0, 5).Split(new string[] { ", " }, StringSplitOptions.None);
                        if (arguments.Count < args.Length) {
                            StringBuilder rgs = new StringBuilder();
                            foreach (string str in args) {
                                if (rgs.ToString() != "") rgs.Append(" ");
                                rgs.Append("<");
                                rgs.Append(str);
                                rgs.Append(">");
                            }
                            player.SendErrorMessage(String.Format("Incorrect arguments! Correct syntax: /timeline start {0} {1}", file, rgs.ToString()));
                            break;
                        }
                    } else {
                        try {
                            Commands.HandleCommand(player, String.Concat(silent ? Commands.SilentSpecifier : Commands.Specifier, line));
                        }
                        catch {
                            Console.WriteLine("Timeline {0} had an error at line {1}.", file, lineNumber);
                        }
                    }
                }
                if (!sentMessage) {
                    player.SendSuccessMessage(String.Format("{0} has started.", file));
                    sentMessage = true;
                }
            }
            if (canClose) {
                tl.Close();
                going = false;
                wait.Stop();
            }
        }
        public void Initialize(CommandArgs e) {
            tl = new StreamReader(Path.Combine(TShock.SavePath, file));
            player = e.Player;
            arguments.Clear();
            for (int i = 2; i < e.Parameters.Count; i++)
                arguments.Add(e.Parameters[i]);
            lineNumber = 0;
            sentMessage = false;
            Start();
        }
        public void Initialize2() {
            wait.AutoReset = false;
            wait.Elapsed += new ElapsedEventHandler(waitTimer);
        }
        private void waitTimer(object source, ElapsedEventArgs e) {
            Start();
        }
    }
}