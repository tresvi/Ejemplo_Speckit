---

description: "Task list for IISLogParser implementation"
---

# Tasks: IISLogParser

**Input**: Design documents from `/specs/001-iis-log-parser/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Test tasks are MANDATORY. Constitution Principle III (Test-First, NON-NEGOTIABLE)
requires every implementation task to have its test written and failing first. Do not omit them.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing
of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single project, per plan.md: `src/IISLogParser/` and `tests/IISLogParser.Tests/` at repository
root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Create the solution and folder skeleton `src/IISLogParser/` and `tests/IISLogParser.Tests/` per plan.md, plus `IISLogParser.sln` at repository root
- [X] T002 Create `src/IISLogParser/IISLogParser.csproj` targeting `net10.0` as `Exe`, referencing `Microsoft.Data.Sqlite` and `Microsoft.Extensions.Logging.Console`
- [X] T003 Create `tests/IISLogParser.Tests/IISLogParser.Tests.csproj` with xUnit, referencing the `IISLogParser` project
- [X] T004 [P] Add `.editorconfig` at repository root enabling nullable reference types and warnings-as-errors for both projects
- [X] T005 [P] Extend `.gitignore` with `bin/`, `obj/`, `*.db`, `*.db-wal`, `*.db-shm` and `sandbox/`
- [X] T006 Verify `dotnet build -c Debug` and `dotnet test` both succeed on the empty solution before writing any feature code

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented.
Covers the CLI surface, the W3C parser, the storage layer and the diagnostics plumbing — every
user story runs through all four.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Command-line surface

- [X] T007 [P] Write failing unit tests for argument parsing (defaults, overrides, unknown option, non-numeric and non-positive interval) in `tests/IISLogParser.Tests/Unit/CliParserTests.cs` per [contracts/cli.md](./contracts/cli.md)
- [X] T008 [P] Write failing contract tests for exit codes 0/1 and `--help` output in `tests/IISLogParser.Tests/Contract/CliContractTests.cs`
- [X] T009 Implement `CliOptions`, `ExitCode`, `HelpText` and `CliParser` in `src/IISLogParser/Cli/` so T007 and T008 pass

### W3C parsing

- [X] T010 [P] Write failing unit tests for `#Fields:` handling — first directive, mid-file replacement, data line before any directive — in `tests/IISLogParser.Tests/Unit/FieldMapTests.cs`
- [X] T011 [P] Write failing unit tests for line parsing rules V-01 through V-06 in `tests/IISLogParser.Tests/Unit/W3CLineParserTests.cs` per [contracts/w3c-log-format.md](./contracts/w3c-log-format.md)
- [X] T012 Implement `FieldMap` (field-name normalisation and column ordering) in `src/IISLogParser/Parsing/FieldMap.cs`
- [X] T013 [P] Implement `LogRecord` with the fixed schema fields plus the extras bag in `src/IISLogParser/Parsing/LogRecord.cs`
- [X] T014 Implement `W3CLineParser`, including `-` to null, `date`+`time` to `timestamp_utc`, and unknown fields to `extra_fields` JSON, in `src/IISLogParser/Parsing/W3CLineParser.cs`

### Storage

- [X] T015 Write failing contract tests asserting the DDL, the unique index and invariants I-01 through I-05 in `tests/IISLogParser.Tests/Contract/DatabaseSchemaContractTests.cs` per [contracts/database-schema.md](./contracts/database-schema.md)
- [X] T016 Implement `SchemaInitializer` with idempotent `IF NOT EXISTS` DDL and the WAL/synchronous pragmas in `src/IISLogParser/Storage/SchemaInitializer.cs`
- [X] T017 Write failing integration tests for batch atomicity, `INSERT OR IGNORE` deduplication and progress upsert in `tests/IISLogParser.Tests/Integration/LogStoreTests.cs`
- [X] T018 [P] Define `ILogStore` and `FileProgress` in `src/IISLogParser/Storage/`
- [X] T019 Implement `SqliteLogStore` writing rows and the progress mark in a single transaction, with prepared commands reused per batch, in `src/IISLogParser/Storage/SqliteLogStore.cs`

### Shared plumbing

- [X] T020 [P] Implement `IFileSystem` and `FileSystem` opening files with `FileShare.ReadWrite | FileShare.Delete` in `src/IISLogParser/Ingestion/`
- [X] T021 [P] Build the `TempLogTree` fixture and reference log files under `tests/IISLogParser.Tests/Fixtures/`, creating a disposable synthetic log tree per test
- [X] T022 [P] Implement `LoggingSetup` emitting one JSON object per line to stderr, and the `OperatorReport` skeleton writing to stdout, in `src/IISLogParser/Diagnostics/`

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Carga completa de los logs existentes (Priority: P1) 🎯 MVP

**Goal**: `IISLogParser --mode snapshot` walks every site folder, parses every log file it finds and
leaves one row per request in a single table, then exits reporting what it did.

**Independent Test**: point the tool at a synthetic tree with two sites and two files each, run the
snapshot, and verify the table holds exactly one row per data line with the correct `site_id` —
quickstart escenarios 1 through 6, 8 and 10.

### Tests for User Story 1 (MANDATORY - Principle III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T023 [P] [US1] Unit tests for site folder recognition (`^W3SVC\d+$`, foreign folders reported as skipped) in `tests/IISLogParser.Tests/Unit/SiteDiscoveryTests.cs`
- [X] T024 [P] [US1] Integration test for a full snapshot over a two-site tree in `tests/IISLogParser.Tests/Integration/SnapshotIngestionTests.cs` (quickstart escenario 1)
- [X] T025 [P] [US1] Integration test asserting a second identical run adds zero rows in `tests/IISLogParser.Tests/Integration/IdempotencyTests.cs` (escenario 2, SC-004)
- [X] T026 [P] [US1] Integration test asserting appended lines are the only ones ingested on re-run in `tests/IISLogParser.Tests/Integration/IncrementalIngestionTests.cs` (escenario 3)
- [X] T027 [P] [US1] Integration test asserting a trailing line without a newline is not ingested until completed, and then exactly once, in `tests/IISLogParser.Tests/Integration/PartialLineTests.cs` (escenario 4, invariant I-03)
- [X] T028 [P] [US1] Integration test asserting a shorter replacement file purges old rows and re-ingests cleanly in `tests/IISLogParser.Tests/Integration/FileReplacementTests.cs` (escenario 5, FR-011a, invariant I-04)
- [X] T029 [P] [US1] Integration test asserting a malformed line is rejected and counted while valid lines still ingest in `tests/IISLogParser.Tests/Integration/MalformedLineTests.cs` (escenario 6, FR-005)
- [X] T030 [P] [US1] Integration test asserting an unreadable file is skipped, reported and yields exit code 2 in `tests/IISLogParser.Tests/Integration/PartialFailureTests.cs` (escenario 8, FR-023 to FR-025)
- [X] T031 [P] [US1] Integration test asserting ingestion succeeds while an external writer holds the file open in `tests/IISLogParser.Tests/Integration/ConcurrentWriterTests.cs` (escenario 10, FR-006)

### Implementation for User Story 1

- [X] T032 [US1] Implement `SiteDiscovery` and `ISiteDiscovery` in `src/IISLogParser/Discovery/` — enumerate `W3SVC<n>` folders, collect foreign folders as skipped
- [X] T033 [US1] Implement `FileIngestor` in `src/IISLogParser/Ingestion/FileIngestor.cs` — read from the recorded offset, consume only newline-terminated lines, flush in 5.000-line batches
- [X] T034 [US1] Add field-map reconstruction on resume to `FileIngestor` — re-scan `#` directives from the start of the file before resuming at the confirmed offset (data-model.md, `FieldMap` transitions)
- [X] T035 [US1] Add truncation and replacement detection to `FileIngestor` — current size below `bytes_ingested` triggers the purge-and-restart path of FR-011a
- [X] T036 [P] [US1] Implement `IngestReport` counters (files, new rows, rejected lines, skipped paths) in `src/IISLogParser/Ingestion/IngestReport.cs`
- [X] T037 [US1] Implement `SnapshotRunner` in `src/IISLogParser/Ingestion/SnapshotRunner.cs` — iterate every site and every file, aggregate the report
- [X] T038 [US1] Add skip-and-continue handling for unreadable files and folders to `SnapshotRunner`, feeding `IngestReport.SkippedPaths` (FR-024, FR-025)
- [X] T039 [US1] Wire the snapshot path in `src/IISLogParser/Program.cs` — compose dependencies, emit the report and map `IngestReport` to exit codes 0, 2 and 3 per [contracts/cli.md](./contracts/cli.md)
- [X] T040 [US1] Emit the `startup`, `file_opened`, `file_replaced`, `field_map_changed`, `batch_committed`, `path_skipped` and `fatal_error` structured events at their boundaries, with no per-line event (Principle IV)

**Checkpoint**: User Story 1 fully functional and testable independently — this is the MVP

---

## Phase 4: User Story 2 - Seguimiento continuo de los logs del día (Priority: P2)

**Goal**: `IISLogParser --mode continuous` stays alive, polls the watched set every *Poll Interval*
seconds and ingests only the new lines, surviving day rollover and shutting down cleanly.

**Independent Test**: start continuous mode against a synthetic tree, append lines to today's file
while it runs, and verify those lines — and only those — reach the table within one cycle;
quickstart escenario 7.

### Tests for User Story 2 (MANDATORY - Principle III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T041 [P] [US2] Unit tests for `u_exYYMMDD.log` and `exYYMMDD.log` date extraction, and for unrecognised names, in `tests/IISLogParser.Tests/Unit/LogFileNameTests.cs`
- [X] T042 [P] [US2] Unit tests for the watched set — today's file per site united with any file holding pending bytes — in `tests/IISLogParser.Tests/Unit/WatchSetResolverTests.cs`
- [X] T043 [P] [US2] Integration test asserting an existing today file is backfilled from the start before the first poll, and previous days are left alone, in `tests/IISLogParser.Tests/Integration/ContinuousBackfillTests.cs` (FR-014a, escenario 7)
- [X] T044 [P] [US2] Integration test asserting three appended lines appear as exactly three new rows on the next cycle in `tests/IISLogParser.Tests/Integration/ContinuousModeTests.cs`
- [X] T045 [P] [US2] Integration test asserting a cycle with no file changes writes nothing in `tests/IISLogParser.Tests/Integration/ContinuousModeTests.cs`
- [X] T046 [P] [US2] Integration test asserting a today file created after startup is picked up without restart in `tests/IISLogParser.Tests/Integration/ContinuousDiscoveryTests.cs` (FR-016)
- [X] T047 [P] [US2] Integration test asserting day rollover switches files without losing the tail of the previous day in `tests/IISLogParser.Tests/Integration/DayRolloverTests.cs` (FR-016, escenario 4 of US2)
- [X] T048 [P] [US2] Integration test asserting cancellation commits the in-flight batch and a later run duplicates nothing in `tests/IISLogParser.Tests/Integration/GracefulShutdownTests.cs` (FR-017)
- [X] T049 [P] [US2] Integration test asserting a skipped path is retried on the following cycle without stopping the process in `tests/IISLogParser.Tests/Integration/PartialFailureTests.cs` (FR-026)
- [X] T050 [P] [US2] Integration test asserting cycles never overlap when a cycle outlasts the poll interval in `tests/IISLogParser.Tests/Integration/CycleOverlapTests.cs`

### Implementation for User Story 2

- [X] T051 [P] [US2] Implement `LogFileName` date parsing for both daily patterns in `src/IISLogParser/Discovery/LogFileName.cs`
- [X] T052 [US2] Implement `WatchSetResolver` in `src/IISLogParser/Discovery/WatchSetResolver.cs` — today's file per site ∪ files with pending bytes
- [X] T053 [P] [US2] Introduce injectable `IClock` and cycle trigger abstractions in `src/IISLogParser/Ingestion/` so tests drive N cycles deterministically without real waits (D-013)
- [X] T054 [US2] Implement `ContinuousRunner` in `src/IISLogParser/Ingestion/ContinuousRunner.cs` — run the cycle, then wait the interval, never overlapping
- [X] T055 [US2] Add the startup backfill path to `ContinuousRunner` — ingest today's file from its progress mark, or from zero when absent, before the first poll (FR-014a)
- [X] T056 [US2] Add graceful shutdown to `ContinuousRunner` — `CancellationToken` honoured at batch boundaries only, wired to `Console.CancelKeyPress` (FR-017, D-010)
- [X] T057 [US2] Add per-cycle retry of previously skipped paths to `ContinuousRunner` (FR-026)
- [X] T058 [US2] Wire the continuous path in `src/IISLogParser/Program.cs` and emit the per-cycle report line, including cycles with all-zero counters
- [X] T059 [US2] Emit the `cycle_completed` and `shutdown` structured events at their boundaries in `src/IISLogParser/Diagnostics/`

**Checkpoint**: User Stories 1 and 2 both work independently

---

## Phase 5: User Story 3 - Ejecución por defecto sin parámetros (Priority: P3)

**Goal**: running the bare executable starts continuous mode at a 10-second interval and prints the
effective configuration it is running with.

**Independent Test**: run the binary with no arguments and confirm the reported effective
configuration is continuous mode and a 10-second interval; quickstart escenario 9.

**Note on scope**: the parsing logic this story asserts lands in Phase 2 (T009), because every
story runs through the CLI. What remains here is the story's own observable behaviour — the
effective-configuration report and the guarantee that a rejected invocation touches nothing.

### Tests for User Story 3 (MANDATORY - Principle III) ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T060 [P] [US3] Contract test asserting a no-argument invocation reports continuous mode and a 10-second interval in `tests/IISLogParser.Tests/Contract/CliDefaultsTests.cs` (FR-018)
- [X] T061 [P] [US3] Contract test asserting `--poll-interval 30` overrides the default in `tests/IISLogParser.Tests/Contract/CliDefaultsTests.cs` (FR-019)
- [X] T062 [P] [US3] Contract test asserting an invalid mode or interval exits with code 1 and creates no database file in `tests/IISLogParser.Tests/Contract/CliContractTests.cs` (FR-020)
- [X] T063 [P] [US3] Contract test asserting the startup block prints all four effective values, and the snapshot-mode variant of the interval line, in `tests/IISLogParser.Tests/Contract/StartupReportTests.cs` (FR-021)

### Implementation for User Story 3

- [X] T064 [US3] Implement the effective-configuration startup block in `src/IISLogParser/Diagnostics/OperatorReport.cs`, including the `10 s (ignorado en modo snapshot)` variant
- [X] T065 [US3] Ensure `src/IISLogParser/Program.cs` validates arguments and resolves the logs path **before** opening or creating the store, so a rejected invocation leaves no database behind (FR-020)

**Checkpoint**: All user stories independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story verification and release readiness

- [X] T066 [P] Add a performance check ingesting a generated 500.000-line reference file and asserting the SC-002 floor in `tests/IISLogParser.Tests/Integration/ThroughputTests.cs`
- [X] T067 [P] Add a test asserting the stderr stream contains no per-line event and that every line is valid JSON carrying `ts`, `level` and `event`, in `tests/IISLogParser.Tests/Contract/StructuredLogContractTests.cs` (Principle IV)
- [X] T068 [P] Walk every scenario in [quickstart.md](./quickstart.md) manually against a real IIS log folder and record any divergence
- [X] T069 Publish with `dotnet publish src/IISLogParser -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` and smoke-test the produced `IISLogParser.exe`
- [X] T070 [P] Update [AGENTS.md](../../AGENTS.md) if any build, test or run command changed during implementation
- [ ] T071 Run `/speckit-converge` and confirm no drift between spec, plan, tasks and code before merging to `main` (Principle V)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational. No dependency on US2 or US3
- **User Story 2 (Phase 4)**: Depends on Foundational, and reuses `FileIngestor` from US1 (T033 to T035). Running US2 before US1 would mean building that ingestor inside US2 instead
- **User Story 3 (Phase 5)**: Depends on Foundational only. Fully independent of US1 and US2 — can be pulled forward at any point after Phase 2
- **Polish (Phase 6)**: Depends on all three stories

### Within Each User Story

- Tests MUST be written and MUST FAIL before implementation (Principle III)
- Parsing and storage before ingestion
- Ingestion before runners
- Runners before `Program.cs` wiring
- Structured-log instrumentation last, over boundaries that already exist

### Critical Path

`T001 → T006 → T009/T014/T019 → T033 → T037 → T039` — everything else hangs off the ingestor and
the snapshot runner.

### Parallel Opportunities

- T004 and T005 in Setup
- T007, T008, T010, T011 — all four failing test suites of Phase 2, written before any Phase 2 implementation
- T018, T020, T021, T022 — independent files with no shared state
- T023 through T031 — the nine US1 test suites, all in distinct files
- T041 through T050 — the ten US2 test suites, all in distinct files
- T060 through T063 — the four US3 contract tests
- US3 (Phase 5) can run fully parallel to US1 and US2 once Phase 2 closes

---

## Parallel Example: User Story 1

```bash
# Write all nine failing test suites for User Story 1 together:
Task: "Unit tests for site folder recognition in tests/IISLogParser.Tests/Unit/SiteDiscoveryTests.cs"
Task: "Integration test for a full snapshot in tests/IISLogParser.Tests/Integration/SnapshotIngestionTests.cs"
Task: "Integration test for idempotency in tests/IISLogParser.Tests/Integration/IdempotencyTests.cs"
Task: "Integration test for incremental ingestion in tests/IISLogParser.Tests/Integration/IncrementalIngestionTests.cs"
Task: "Integration test for the partial trailing line in tests/IISLogParser.Tests/Integration/PartialLineTests.cs"
Task: "Integration test for file replacement in tests/IISLogParser.Tests/Integration/FileReplacementTests.cs"
Task: "Integration test for malformed lines in tests/IISLogParser.Tests/Integration/MalformedLineTests.cs"
Task: "Integration test for partial failure in tests/IISLogParser.Tests/Integration/PartialFailureTests.cs"
Task: "Integration test for the concurrent writer in tests/IISLogParser.Tests/Integration/ConcurrentWriterTests.cs"

# Then run them and confirm every one fails before writing T032 onward.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run quickstart escenarios 1 through 6, 8 and 10
5. At this point the tool already replaces manual log analysis: every historical log, every site,
   one queryable table

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Add User Story 1 → validate → **MVP**
3. Add User Story 2 → validate → near-real-time monitoring
4. Add User Story 3 → validate → zero-argument convenience
5. Phase 6 → performance, log-shape verification, publish

### Parallel Team Strategy

Once Phase 2 closes, US3 is genuinely independent and can be taken by a second developer straight
away. US2 is better sequenced after US1, because it reuses the file ingestor built there — running
them truly in parallel means one of the two builds that ingestor and the other waits or duplicates
it.

---

## Notes

- [P] tasks = different files, no dependencies
- Verify every test fails before writing the implementation it covers — the red step is the point,
  not a formality
- Commit after each task or logical group; the phase itself closes in its own commit (Principle V)
- Stop at any checkpoint to validate a story independently
- The single-transaction invariant (I-01) is the one thing that cannot be relaxed for convenience:
  if rows and the progress mark ever commit separately, the tool silently loses or duplicates
  records under crash, and no test above will catch it after the fact
