"""Read-only acceptance summary; never launches a host or repeats a transfer."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('run', type=Path)
args = parser.parse_args()
run = args.run.resolve()
state = json.loads((run / 'checkpoint.json').read_text(encoding='utf-8-sig'))
rows = []
missing = []
previews = []
for directory in sorted(run.glob('attempt-*')):
    telemetry_path = directory / 'telemetry-summary.json'
    if not telemetry_path.exists():
        continue
    telemetry = json.loads(telemetry_path.read_text(encoding='utf-8-sig'))
    for name in ('import', 'mbox', 'eml', 'archive'):
        time_path = directory / (name + '-preview-time.json')
        if not time_path.exists():
            continue
        timing = json.loads(time_path.read_text(encoding='utf-8-sig'))
        memory = telemetry['phases'].get(name + '-preview')
        previews.append({'phase': name + '-preview', 'attempt': directory.name,
                         **timing, 'telemetry': memory})
        if timing['elapsedSec'] >= 1 and (not memory or not memory['samples']):
            missing.append(name + ': long preview has no memory samples')
for name in ('import', 'mbox', 'eml', 'archive'):
    phase = state.get('phases', {}).get(name, {})
    if not phase.get('complete'):
        missing.append(name + ': incomplete')
    segments = phase.get('segments', [])
    samples = []
    phase_details = {}
    for segment in segments:
        directory = run / segment['attempt']
        telemetry_path = directory / 'telemetry-summary.json'
        if not telemetry_path.exists():
            missing.append(name + ': missing telemetry for ' + segment['attempt'])
            continue
        telemetry = json.loads(telemetry_path.read_text(encoding='utf-8-sig'))
        for label, values in telemetry['phases'].items():
            if label in (name + '-starting', name + '-transfer', name + '-verifying'):
                phase_details[segment['attempt'] + '/' + label] = values
                if values['samples']:
                    samples.append(values)
    if not samples:
        missing.append(name + ': no active phase memory samples')
    if name == 'import' and not state['small']:
        final_samples = sum(value['samples'] for label, value in phase_details.items()
                            if label.endswith('/import-verifying'))
        if not final_samples:
            missing.append('import: final target verification memory unavailable')
    rows.append({
        'phase': name, 'jobId': phase.get('jobId'), 'complete': phase.get('complete', False),
        'segments': len(segments), 'recovered': phase.get('recovered', False),
        'measuredSegmentSeconds': sum(s['elapsedSec'] for s in segments) if segments else None,
        'measuredHostCpuSeconds': sum(s['cpuSec'] for s in segments) if segments else None,
        'peakWsBytes': max((s['peakWsBytes'] for s in samples), default=None),
        'peakPrivateBytes': max((s['peakPrivateBytes'] for s in samples), default=None),
        'sampleCount': sum(s['samples'] for s in samples),
        'phaseTelemetry': phase_details,
    })
if not state.get('integrityPass'):
    missing.append('independent integrity oracle incomplete')
print(json.dumps({'runId': state['runId'], 'small': state['small'],
                  'integrityPass': state.get('integrityPass', False),
                  'measurementReady': not missing, 'missing': missing,
                  'previews': previews, 'rows': rows},
                 ensure_ascii=False, indent=2))
