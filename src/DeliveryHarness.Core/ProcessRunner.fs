namespace DeliveryHarness.Core

open System
open System.Diagnostics
open System.Threading.Tasks

module ProcessRunner =

    let private await (task: Task) =
        task.GetAwaiter().GetResult()

    let private awaitResult (task: Task<'T>) =
        task.GetAwaiter().GetResult()

    let private buildStartInfo cwd fileName (args: string list) =
        let startInfo = ProcessStartInfo()
        startInfo.FileName <- fileName
        startInfo.WorkingDirectory <- cwd
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true

        for arg in args do
            startInfo.ArgumentList.Add arg

        startInfo

    let private appendTimeoutMessage timeoutMs (stderr: string) =
        let timeoutMessage = sprintf "Process timed out after %d ms." timeoutMs

        if String.IsNullOrWhiteSpace stderr then
            timeoutMessage
        else
            String.concat Environment.NewLine [ stderr; timeoutMessage ]

    let run (cwd: string) (timeoutMs: int) (fileName: string) (args: string list) : ExecResult =
        let startInfo = buildStartInfo cwd fileName args

        try
            use proc = new Process()
            proc.StartInfo <- startInfo

            let started = proc.Start()

            if not started then
                { ExitCode = -1
                  StdOut = ""
                  StdErr = sprintf "Failed to start process '%s'." fileName
                  TimedOut = false }
            else
                let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                let stderrTask = proc.StandardError.ReadToEndAsync()
                let waitTask = proc.WaitForExitAsync()
                let completedTask = Task.WhenAny(waitTask, Task.Delay timeoutMs) |> awaitResult
                let finished = Object.ReferenceEquals(completedTask, waitTask)

                if finished then
                    await waitTask

                    { ExitCode = proc.ExitCode
                      StdOut = awaitResult stdoutTask
                      StdErr = awaitResult stderrTask
                      TimedOut = false }
                else
                    try
                        if not proc.HasExited then
                            proc.Kill(true)
                    with _ ->
                        ()

                    try
                        if not proc.HasExited then
                            proc.WaitForExit()
                    with _ ->
                        ()

                    { ExitCode = if proc.HasExited then proc.ExitCode else -1
                      StdOut = awaitResult stdoutTask
                      StdErr = awaitResult stderrTask |> appendTimeoutMessage timeoutMs
                      TimedOut = true }
        with ex ->
            { ExitCode = -1
              StdOut = ""
              StdErr = sprintf "Failed to start process '%s': %s" fileName ex.Message
              TimedOut = false }

    let runShell (cwd: string) (timeoutMs: int) (script: string) =
        if String.IsNullOrWhiteSpace script then
            { ExitCode = 0
              StdOut = ""
              StdErr = ""
              TimedOut = false }
        else if OperatingSystem.IsWindows() then
            run cwd timeoutMs "cmd.exe" [ "/C"; script ]
        else
            run cwd timeoutMs "/bin/sh" [ "-c"; script ]
