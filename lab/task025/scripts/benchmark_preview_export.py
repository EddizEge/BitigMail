#!/usr/bin/env python3
"""TASK-025 bounded preview/export benchmark for the preserved 128 MiB target."""

# Keep the accepted TASK-022 oracle and lifecycle implementation as the single
# source of truth; only redirect this run's evidence and task label.
import importlib.util
import pathlib

script = pathlib.Path(__file__).resolve().parents[2] / "task022" / "scripts" / "benchmark_preview_export.py"
spec = importlib.util.spec_from_file_location("task025_benchmark", script)
benchmark = importlib.util.module_from_spec(spec)
spec.loader.exec_module(benchmark)
benchmark.EVIDENCE_DIR = benchmark.ROOT / ".codex-coordination/evidence/TASK-025" / benchmark.RUN_ID
benchmark.EVIDENCE_DIR.mkdir(parents=True, exist_ok=True)
benchmark.base_pipeline.EVIDENCE_DIR = benchmark.EVIDENCE_DIR

original_main = benchmark.benchmark_main

def benchmark_main():
    original_save_json = benchmark.save_json

    def task025_save_json(name, data):
        if name == "summary.json" and isinstance(data, dict):
            data = {**data, "taskId": "TASK-025"}
        original_save_json(name, data)

    benchmark.save_json = task025_save_json
    benchmark.base_pipeline.save_json = task025_save_json
    original_main()

if __name__ == "__main__":
    benchmark_main()
