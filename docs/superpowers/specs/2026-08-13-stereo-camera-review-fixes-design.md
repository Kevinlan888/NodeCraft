# Stereo Camera Review Fixes Design

**Date:** 2026-08-13  
**Branch:** `feature/stereo-camera-streaming`

## Goal

Close the lifecycle, WPF, native-handle, exception-boundary, configuration, preview-rendering, logging, and error-display issues identified in the stereo-camera review, while preserving the existing Windows-only streaming contract: one-shot execution releases resources after one iteration and continuous execution consumes the newest complete color/depth frame.

## Scope

The change covers the nine review findings:

1. Make graph-session startup and stop/dispose race-safe.
2. Make both plugin WPF content factories transfer parsed content without violating logical parenting.
3. Ensure the Stop menu event consumes its final asynchronous exception boundary.
4. Preserve a primary camera-start exception when best-effort cleanup also fails.
5. Release every native image handle on every image-acquisition/conversion failure path.
6. Treat the camera IP address as persistent configuration rather than a runtime DAG input.
7. Make the latest-only preview worker handoff and UI generation checks race-safe.
8. Remove synchronous per-frame information-level file logging.
9. Keep full diagnostics in logs while showing a bounded user-facing error message.

No exposure controls, new camera outputs, vendor protocol changes, or non-Windows support are included.

## Design

### 1. Graph lifecycle state machine

`GraphExecutionSession` remains the owner of lifecycle node start/stop ordering. Startup receives a linked token that includes both the caller token and the session stop token. `StopAsync` transitions to `Stopping` under the state lock, cancels that token, captures the current start task, then waits for startup outside the lock before taking the lifecycle snapshot. `StartCoreAsync` checks the state after every awaited lifecycle start and before publishing `Running`.

If a lifecycle start completes after stopping has begun, it is registered under the state lock and immediately stopped by the stop path; the session never publishes `Running` after `Stopping` or `Stopped`. Stop remains idempotent and cleanup still runs in reverse start order. `DisposeAsync` continues to await the same stop task before disposing synchronization primitives.

### 2. WPF content ownership

The embedded XAML root is parsed as a `UserControl` so its namescope can be queried. Before assigning its child to the plugin view, the root's `Content` is set to `null`; the detached child is then assigned to the actual view. This preserves existing `FindName` lookups while ensuring the child has exactly one logical parent. Both camera editor and image preview view use the same pattern.

### 3. Error boundaries and exception precedence

`FlowPage.StopExecutionAsync` continues logging failures and rethrowing for task-based callers. `MainWindow.MenuStop_Click` catches the rethrown exception and displays a notification, preventing an `async void` exception from reaching the WPF dispatcher as unhandled. The window-closing path keeps its existing catch behavior.

`StereoCameraCaptureSession.StartCoreAsync` stores the primary startup exception, performs cleanup in a nested best-effort block, logs any cleanup exception, and rethrows the primary exception with its original stack. Cleanup errors remain observable in logs and in explicit stop operations.

### 4. Native image ownership

Each non-null result of `scGetFrameImage` is wrapped in `StereoCameraImageHandle` before the next native acquisition or image conversion. The handles are disposed in the method scope, so a failed depth acquisition, failed color conversion, failed depth conversion, or failed buffer copy releases every acquired native resource.

### 5. Persistent IP configuration

The camera IP remains serialized in `WorkflowNode.Inputs` through `StereoCameraNodeModel`, but it is removed from the connectable `FlowNodeDefinition.InputPorts`/runtime model port list. This prevents graph links from being created to a value consumed before DAG execution. Existing persisted IP values continue to be read by `StereoCameraExecutor`; connected runtime values are rejected by graph-model reconciliation/validation rather than failing during camera startup.

### 6. Latest-only preview rendering

The queue assigns each worker a generation. A worker may clear `_workerTask` only if it still owns the current generation, preventing an old worker's `finally` block from erasing a newer worker reference. The queue retains one pending item and continues dropping older frames.

The view checks both unload state and the submitted version immediately before UI mutation on the dispatcher. A result that became stale while waiting for the dispatcher is discarded.

### 7. Logging and user-facing errors

Per-iteration start/finish messages and per-node iteration diagnostics are lowered to `Trace` so the existing Debug file rule does not synchronously write them at camera frame rate. Session-level errors and validation information remain at their current levels.

`ReportExecutionFailure` logs the complete exception including stack and inner exceptions, but the result panel receives only the supplied stage plus a bounded exception message. This avoids exposing absolute paths and implementation details in normal UI output.

## Testing strategy

Tests are added before each production change and must demonstrate a failure against the current implementation:

- A blocked lifecycle start stopped concurrently cannot revive the session and all late-started resources are stopped.
- Both content factories can be instantiated on an STA thread without a logical-parent exception.
- A stop cleanup failure is consumed by the menu event boundary.
- A startup failure remains the observed exception when cleanup also fails.
- Fault-injected native image acquisition/conversion releases every handle.
- A linked IP configuration is rejected before session startup while a persisted IP still starts normally.
- Preview worker handoff never creates overlapping workers or loses the active worker reference; a stale result cannot mutate the UI after a newer version is submitted.
- Iteration logging is below the configured Debug threshold and user-facing error text excludes stack/path details.

Existing normal lifecycle, latest-frame, plugin loading, and Windows integration tests remain required. Final verification includes `git diff --check`, the complete Windows x64 test suite, self-contained publish/package validation, and archive hash/integrity checks.

## Acceptance criteria

- No review item remains unaddressed in the changed source or tests.
- Session state cannot transition from stopping/stopped back to running.
- All acquired native image handles are released on success and failure paths.
- Camera nodes still emit synchronized color, depth, color-calibration, and depth-calibration outputs.
- One-shot and continuous execution behavior is unchanged except for safer cleanup and reduced logging overhead.
- The Windows x64 package loads the plugin without the previous `kernel32.dll` resolver failure and contains no duplicate private framework/vendor assemblies.
