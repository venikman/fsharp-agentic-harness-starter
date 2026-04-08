namespace DeliveryHarness.Core

open System

type IssueState =
    | Todo
    | InProgress
    | HumanReview
    | Rework
    | Merging
    | Done
    | Closed
    | Cancelled
    | Other of string
    with
        member this.AsText =
            match this with
            | Todo -> "Todo"
            | InProgress -> "In Progress"
            | HumanReview -> "Human Review"
            | Rework -> "Rework"
            | Merging -> "Merging"
            | Done -> "Done"
            | Closed -> "Closed"
            | Cancelled -> "Cancelled"
            | Other value -> value

        static member Parse(value: string) =
            match value.Trim().ToLowerInvariant() with
            | "todo" -> Todo
            | "in progress"
            | "in_progress"
            | "inprogress" -> InProgress
            | "human review"
            | "human_review"
            | "humanreview" -> HumanReview
            | "rework" -> Rework
            | "merging" -> Merging
            | "done" -> Done
            | "closed" -> Closed
            | "cancelled"
            | "canceled" -> Cancelled
            | other -> Other value

type HookSet =
    { AfterCreate: string option
      BeforeRun: string option
      AfterRun: string option
      BeforeRemove: string option
      TimeoutMs: int }

type WorkflowConfig =
    { ProjectRoot: string
      TrackerKind: string
      ProjectKey: string
      TrackerPath: string
      ActiveStates: string list
      TerminalStates: string list
      WorkspaceRoot: string
      CleanupTerminalWorkspaces: bool
      PollIntervalSeconds: int
      MaxConcurrency: int
      MaxAttempts: int
      AgentCommand: string
      AgentArgs: string list
      AgentMaxTurns: int
      AgentTimeoutMs: int
      Hooks: HookSet }

type WorkflowDefinition =
    { FilePath: string
      PromptTemplate: string
      Config: WorkflowConfig }

type TrackerIssue =
    { Id: string
      Title: string
      Description: string
      State: IssueState
      Priority: int
      Acceptance: string list
      Validation: string list
      Constraints: string list
      UpdatedAtUtc: DateTimeOffset
      SourcePath: string }

type WorkspaceInfo =
    { Key: string
      Path: string
      CreatedNow: bool }

type ExecResult =
    { ExitCode: int
      StdOut: string
      StdErr: string
      TimedOut: bool }

type AgentOutcome =
    { Succeeded: bool
      Summary: string
      EvidencePaths: string list
      TranscriptPath: string option }

type RunRecord =
    { IssueId: string
      IssueTitle: string
      WorkspacePath: string
      StartedAtUtc: DateTimeOffset
      FinishedAtUtc: DateTimeOffset
      Status: string
      Summary: string
      EvidencePaths: string list }
