namespace DeliveryHarness.Core

open System
open System.Diagnostics

module ProcessRunner =

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

    let run (cwd: string) (timeoutMs: int) (fileName: string) (args: string list) : ExecResult =
        let startInfo = buildStartInfo cwd fileName args

        use process = new Process()
        process.StartInfo <- startInfo

        let started = process.Start()

        if not started then
            { ExitCode = -1
              StdOut = ""
              StdErr = sprintf "Failed to start process '%s'." fileName
              TimedOut = false }
        else
            let finished = process.WaitForExit timeoutMs

            if not finished then
                try
                    process.Kill(true)
                with _ ->
                    ()

                { ExitCode = -1
                  StdOut = process.StandardOutput.ReadToEnd()
                  StdErr =
                    let current = process.StandardError.ReadToEnd()

                    if String.IsNullOrWhiteSpace current then
                        "Process timed out."
                    else
                        current
                  TimedOut = true }
            else
                { ExitCode = process.ExitCode
                  StdOut = process.StandardOutput.ReadToEnd()
                  StdErr = process.StandardError.ReadToEnd()
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
            run cwd timeoutMs "/bin/sh" [ "-lc"; script ]
