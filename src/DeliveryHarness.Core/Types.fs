namespace DeliveryHarness.Core

open System

type IssueState =
    | Todo
    | InProgress
    | Blocked
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
            | Blocked -> "Blocked"
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
            | "blocked" -> Blocked
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
      TrackerApiUrl: string option
      TrackerApiKey: string option
      TrackerApiKeyIsEnvBacked: bool
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
      SensitiveValues: string list
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

type TrackerPort =
    { ListIssues: unit -> Result<TrackerIssue list, string>
      ListCandidateIssues: unit -> Result<TrackerIssue list, string>
      TryFindById: string -> Result<TrackerIssue option, string>
      TryRefreshById: string -> Result<TrackerIssue option, string>
      ListTerminalIssues: unit -> Result<TrackerIssue list, string> }

type WorkspaceInfo =
    { Key: string
      Path: string
      CreatedNow: bool }

type ExecResult =
    { ExitCode: int
      StdOut: string
      StdErr: string
      TimedOut: bool
      Cancelled: bool }

type AgentOutcome =
    { Succeeded: bool
      Cancelled: bool
      Summary: string
      EvidencePaths: string list
      TranscriptPath: string option }

type RunPerformer =
    { Role: string
      Identity: string }

type RunContext =
    { ProjectRoot: string
      WorkflowPath: string
      TrackerKind: string
      ProjectKey: string }

type HookOutcome =
    { Name: string
      Status: string
      Summary: string }

type RunRecord =
    { IssueId: string
      IssueTitle: string
      WorkspacePath: string
      StartedAtUtc: DateTimeOffset
      FinishedAtUtc: DateTimeOffset
      Status: string
      Summary: string
      EvidencePaths: string list
      AttemptNumber: int
      TurnNumber: int
      Performer: RunPerformer
      MethodDescriptionPaths: string list
      Context: RunContext
      ValidationVerdict: string
      HookOutcomes: HookOutcome list }

type RuntimeLogEntry =
    { TimestampUtc: DateTimeOffset
      Level: string
      EventType: string
      Message: string
      IssueId: string option
      AttemptNumber: int option
      TurnNumber: int option
      RecordPath: string option
      WorkspacePath: string option }

type RunningIssueStatus =
    { IssueId: string
      IssueTitle: string
      IssueState: string
      AttemptNumber: int
      TurnNumber: int
      StartedAtUtc: DateTimeOffset
      WorkspacePath: string }

type RetryIssueStatus =
    { IssueId: string
      IssueTitle: string
      IssueState: string
      NextAttemptNumber: int
      NextAttemptAtUtc: DateTimeOffset
      LastSummary: string }

type HostStatusSnapshot =
    { GeneratedAtUtc: DateTimeOffset
      WorkflowPath: string
      LogPath: string
      ActiveStates: string list
      TerminalStates: string list
      LastReloadAttemptAtUtc: DateTimeOffset option
      LastReloadError: string option
      RunningIssues: RunningIssueStatus list
      RetryingIssues: RetryIssueStatus list }
