# TASK-028 real two-job queue evidence

Captured from the completed TestingHost acceptance run; the queue was not rerun to create this package.

## Initial API observations

- First response: `job-582f3e6c5ad0`, status `converting`.
- Second response: `job-7e3670848f2f`, status `queued`, `waitingAtShutdown=true`.
- These response-time observations were recorded by the SOL controller during the run. The durable final records below naturally contain terminal state and therefore do not retain the transient initial status.

## Durable terminal records

- `job-582f3e6c5ad0.final.json`: completed, 13/13, failed 0; started `2026-09-15T14:46:54.4283248+00:00`, completed `2026-09-15T14:46:55.068864+00:00`.
- `job-7e3670848f2f.final.json`: completed, 13/13, failed 0; started `2026-09-15T14:46:55.0823734+00:00`, completed `2026-09-15T14:46:55.5527314+00:00`.
- Job 2 started strictly after Job 1 completed.

## Reports and artifacts

- Both report files record `overallStatus=SUCCESS`, `fidelityStatus=PASS`, `sourceHashMatch=true`, and zero failed items.
- Source SHA-256 before and after: `B0801758A2E61D4CE6E86799701A81A7A60C38401F73B13C993D94C03A2EE57A`.
- First PST: 271,360 bytes, SHA-256 `73F80F0343D339FEB459629AFD0776AC9798BA230E3C2AA287EAF4A57C3E5E14`.
- Second PST: 271,360 bytes, SHA-256 `A8D8F439E242FEEA6EE6CB327D74BDF878B1CD55E9317DDB975AC555559D6FC`.

## Evidence file integrity

- `job-582f3e6c5ad0.final.json`: `EFD5DCA027E58100EFED8CBE13715B9956B58757864884FD573DCF4BEB308021`
- `job-7e3670848f2f.final.json`: `B245CDCB39D670F4ED52226F3E223EB61020FF70538D9021DC133171A5267CB0`
- `report-job-582f3e6c5ad0.json`: `844C3BC02CE07D250A29BA357CA20CEA9B2B9398A08677620A28B3480A31A5AF`
- `report-job-7e3670848f2f.json`: `6A9AE842684041F00F61C29494FD98644DEDF3418E8ADF0E0F92A9800F63B9BF`

## TestingHost lifecycle

- Owned acceptance process: PID 2464, stopped after the queue run.
- Owned UI-verification process: PID 51496, stopped after the rendered check.
- Port 6175 was independently checked and had no listener after acceptance.
