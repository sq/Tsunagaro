using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tsunagaro.Restart {
    class Program {
        static void Main (string[] args) {
            if (args.Length < 2) {
                Console.WriteLine("usage: Restart delay command [command_arguments]");
                Environment.Exit(1);
            }

            var delay = double.Parse(args[0]);
            var command = args[1].Trim();
            var fullCommandLine = Environment.CommandLine;
            var searchIndex = fullCommandLine.IndexOf(command) + command.Length;
            var commandArguments = fullCommandLine.Substring(searchIndex).Trim();

            Console.WriteLine("Waiting {0} second(s)...", delay);
            Thread.Sleep(TimeSpan.FromSeconds(delay));

            Console.WriteLine("Running '{0}' {1}...", command, commandArguments);
            var psi = new ProcessStartInfo(command, commandArguments) {
                UseShellExecute = false
            };
            Process.Start(psi);

            Environment.Exit(0);
        }
    }
}
