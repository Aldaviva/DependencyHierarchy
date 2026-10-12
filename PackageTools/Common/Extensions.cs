namespace PackageTools.Common;

public static class Extensions {

    extension(Process) {

        /// <inheritdoc cref="Extensions.StartAndWaitForExit(string,System.Collections.Generic.IEnumerable{string})" />
        public static async Task<int> StartAndWaitForExit(string filename, IEnumerable<string> arguments, CancellationToken cancellationToken) {
            using Process process = Process.Start(filename, arguments);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }

        /// <summary>Starts the process, allows its standard streams to pipe to the console so they will appear by default, wait for it to exit, and return the exit code.</summary>
        /// <param name="filename">Program file to start.</param>
        /// <param name="arguments">Zero or more command-line arguments to pass to the program.</param>
        /// <returns>Process exit code.</returns>
        public static Task<int> StartAndWaitForExit(string filename, params IEnumerable<string> arguments) =>
            Process.StartAndWaitForExit(filename, arguments, CancellationToken.None);

    }

}