"""Extract write-once phase CSVs from an existing completed attempt; no host calls."""
import argparse
import csv
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser()
parser.add_argument('attempt', type=Path)
parser.add_argument('destination', type=Path)
args = parser.parse_args()
source = args.attempt.resolve()
destination = args.destination.resolve()
if not (source / 'telemetry-summary.json').is_file():
    raise SystemExit('Attempt telemetry has not been finalized')
destination.mkdir(parents=True, exist_ok=False)
handles = {}
writers = {}
counts = {}
try:
    with (source / 'telemetry.csv').open(encoding='utf-8-sig', newline='') as stream:
        rows = csv.DictReader(stream)
        for row in rows:
            phase = row['phase']
            if not re.fullmatch(r'[a-z0-9-]+', phase):
                raise ValueError('Unexpected phase label')
            if phase not in writers:
                handles[phase] = (destination / (phase + '.csv')).open('x', encoding='utf-8', newline='')
                writers[phase] = csv.DictWriter(handles[phase], fieldnames=rows.fieldnames)
                writers[phase].writeheader()
                counts[phase] = 0
            writers[phase].writerow(row)
            counts[phase] += 1
finally:
    for handle in handles.values():
        handle.close()
summary = json.loads((source / 'telemetry-summary.json').read_text(encoding='utf-8-sig'))
discrepancies = {phase: {'csv': counts.get(phase, 0), 'summary': values['samples']}
                 for phase, values in summary['phases'].items()
                 if counts.get(phase, 0) != values['samples']}
with (destination / 'index.json').open('x', encoding='utf-8') as stream:
    json.dump({'sourceAttempt': str(source), 'phaseSamples': counts,
               'summaryAgreement': not discrepancies, 'discrepancies': discrepancies}, stream, indent=2)
if discrepancies:
    raise SystemExit('CSV/summary sample counts differ; retained evidence requires review')
print(json.dumps({'destination': str(destination), 'phases': len(counts), 'samples': sum(counts.values())}))
