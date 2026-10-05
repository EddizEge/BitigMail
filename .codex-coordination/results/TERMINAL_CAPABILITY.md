# Antigravity terminal capability

## Official CLI

- Official references: `https://antigravity.google/docs/cli/headless/` and `https://www.antigravity.google/docs/cli/install/`.
- Command: `agy`
- Installed binary: `C:\Users\Eddiz\AppData\Local\agy\bin\agy.exe`
- The binary is on `PATH`; both `Get-Command agy` and `where agy` resolved the same path.
- `agy --help` exited `0` and documents noninteractive `-p`/`--print`, `--output-format json`, `--model`, `--mode accept-edits`, and `--print-timeout`.
- `agy models` exited `0` using the existing session and listed `gemini-3.8-flash-high` as `Gemini 3.8 Flash (High)`.
- This official CLI is separate from the earlier internal `Antigravity.exe` headless mechanism. The latter's isolated OAuth failure is not an `agy` limitation.

## Verified command shape

```powershell
agy -p '<prompt>' --output-format json --model gemini-3.8-flash-high --mode accept-edits --print-timeout 2m
```

The successful TASK-002 invocation used the installed absolute binary path and explicitly instructed Gemini to use direct file editing rather than terminal/`RunCommand`.

## Execution result

- First official CLI attempt: process exit `0`, JSON status `SUCCESS`, but `RunCommand` was auto-denied because headless mode could not prompt for command permission; no file was created.
- Recovery attempt: process exit `0`, JSON status `SUCCESS`, conversation ID `4c1cf464-06cf-4eaa-b2c4-3de8456a9200`; Gemini reported direct file creation without terminal commands.
- Model evidence: the exact invoked slug was `gemini-3.8-flash-high`, and `agy models` maps it to `Gemini 3.8 Flash (High)`.
- Artifact: `.codex-coordination/results/TASK-002-gemini-proof.json`.
- Deterministic JSON assertions passed with exit status `0`.

## Authentication and safety

- Existing `agy` authentication was sufficient; `agy models` and both print-mode calls ran without login prompts.
- No UI/browser interaction, installation, login, credential inspection/change, settings change, or `--dangerously-skip-permissions` was used.
- A prompt requiring terminal commands may still be auto-denied unless a pre-existing allow-rule permits it. Direct workspace file editing succeeded without changing permissions.

## Recommended operating mode

Use official `agy -p` print mode with an explicit Gemini model slug, structured output, bounded `--print-timeout`, and narrowly scoped prompts. Prefer direct file-edit tools for workspace changes; never use `--dangerously-skip-permissions` under this controller protocol.
