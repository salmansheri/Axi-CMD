#!/usr/bin/env python3
"""Prove aged block/chunk deletion in an isolated copy of the pinned backends.
No published ports and no access to the developer stack's volume.
"""
import base64
import json
from datetime import datetime, timezone
import shutil
import subprocess
import tempfile
import time
import uuid
from pathlib import Path
from urllib.parse import urlencode

ROOT = Path(__file__).resolve().parent
IMAGE = json.loads(subprocess.check_output(['docker', 'compose', '-f', str(ROOT / 'compose.yaml'), 'config', '--format', 'json']))['services']['lgtm']['image']
NAME = 'axiapi-retention-' + uuid.uuid4().hex[:12]


def docker(*args, **kwargs):
    return subprocess.check_output(['docker', *args], text=True, **kwargs)


def curl(port, path, payload=None):
    args = ['exec', '-i', NAME, 'curl', '-fsS', '--max-time', '10']
    if payload is not None:
        args += ['-H', 'Content-Type: application/json', '--data-binary', '@-']
    return docker(*args, f'http://localhost:{port}{path}', input=json.dumps(payload) if payload is not None else None, stderr=subprocess.DEVNULL)


def until(predicate, description, timeout=300):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if docker("inspect", "--format", "{{.State.Status}}", NAME).strip() == "exited":
            raise RuntimeError("Isolated container exited: " + docker("logs", NAME))
        try:
            docker('exec', NAME, 'chmod', '-R', 'a+rwX', '/data', stderr=subprocess.DEVNULL)
            if predicate():
                print('PASS: ' + description, flush=True)
                return
        except (subprocess.CalledProcessError, ValueError, OSError):
            pass
        time.sleep(2)
    raise RuntimeError(description)


with tempfile.TemporaryDirectory(prefix='axiapi-retention-') as folder:
    fixture = Path(folder)
    fixture.chmod(0o777)
    for name in ('loki.yaml', 'tempo.yaml'):
        shutil.copy(ROOT / name, fixture / name)
    loki = (fixture / 'loki.yaml').read_text().replace('compaction_interval: 10m', 'compaction_interval: 2s').replace('retention_delete_delay: 2h', 'retention_delete_delay: 30s').replace('  retention_period: 72h', '  retention_period: 72h\n  reject_old_samples: false').replace('\ningester:\n', '\ningester:\n  chunk_idle_period: 1s\n  max_chunk_age: 1s\n  flush_check_period: 1s\n')
    # Fixtures are explicitly flushed; disable replay so aged injected logs are not ingested again.
    loki = loki.replace('    dir: /data/loki/wal', '    dir: /data/loki/wal\n    enabled: false')
    loki += '\nstorage_config:\n  tsdb_shipper:\n    resync_interval: 2s\n    cache_ttl: 2s\n    ingesterdbretainperiod: 2s\n'
    loki += '\nchunk_store_config:\n  chunk_cache_config:\n    default_validity: 2s\n    embedded_cache:\n      enabled: true\n      ttl: 2s\n'
    (fixture / 'loki.yaml').write_text(loki)
    tempo = (fixture / 'tempo.yaml').read_text().replace('interval: 10m', 'interval: 2s').replace('  trace:\n', '  trace:\n    blocklist_poll: 2s\n').replace('live_store:\n', 'live_store:\n  max_trace_idle: 1s\n  max_trace_live: 2s\n  max_block_duration: 2s\n  complete_block_timeout: 2s\n  block_reclaim_grace: 1s\n').replace('backend_scheduler:\n', 'backend_scheduler:\n  maintenance_interval: 2s\n  backend_flush_interval: 2s\n')
    tempo += '\nbackend_worker:\n  compaction:\n    compacted_block_retention: 2s\n'
    (fixture / 'tempo.yaml').write_text(tempo)
    now = time.time()
    (fixture / 'metrics.txt').write_text(f'# HELP retention_fixture Aged retention fixture\n# TYPE retention_fixture gauge\nretention_fixture{{age="old"}} 1 {now - 4 * 86400}\nretention_fixture{{age="fresh"}} 1 {now}\n# EOF\n')
    command = 'set -e; mkdir -p /data/prometheus; if [ ! -d /fixture/initial-prometheus ]; then ./prometheus/promtool tsdb create-blocks-from openmetrics /fixture/metrics.txt /data/prometheus; cp -r /data/prometheus /fixture/initial-prometheus; chmod -R a+rwX /fixture/initial-prometheus; fi; exec ./run-all.sh'
    try:
        docker('run', '-d', '--name', NAME, '-v', f'{folder}:/fixture:Z', '-v', f'{folder}/data:/data:Z', '-v', f'{folder}/loki.yaml:/otel-lgtm/loki-config.yaml:ro,Z', '-v', f'{folder}/tempo.yaml:/otel-lgtm/tempo-config.yaml:ro,Z', '-e', 'ENABLE_LOGS_LOKI=true', '-e', 'ENABLE_LOGS_TEMPO=true', '-e', 'PROMETHEUS_EXTRA_ARGS=--storage.tsdb.retention.time=3d', '--entrypoint', 'bash', IMAGE, '-c', command)
        until(lambda: bool(curl(3100, '/ready')) and bool(curl(3200, '/ready')) and bool(curl(9090, '/-/ready')), 'Isolated backends are ready')
        initial = {p.parent.name: json.loads(p.read_text()) for p in (fixture / 'initial-prometheus').glob('*/meta.json')}
        old_blocks = [key for key, meta in initial.items() if meta['maxTime'] < (now - 3 * 86400) * 1000]
        fresh_blocks = [key for key in initial if key not in old_blocks]
        assert old_blocks and fresh_blocks, 'promtool must create both fixture blocks'
        def metric(age):
            query = urlencode({'query': 'retention_fixture{age="' + age + '"}', 'time': str(now - (4 * 86400 if age == 'old' else 0) + 1)})
            return json.loads(curl(9090, '/api/v1/query?' + query))['data']['result']
        until(lambda: all(not (fixture / 'data/prometheus' / key).exists() for key in old_blocks) and not metric('old') and bool(metric('fresh')), 'Prometheus deletes the known aged fixture blocks and keeps the fresh metric queryable')
        old = str(int((now - 4 * 86400) * 1e9))
        fresh = str(int(now * 1e9))
        curl(3100, '/loki/api/v1/push', {'streams': [{'stream': {'retention_fixture': 'old'}, 'values': [[old, 'retention-old']]}, {'stream': {'retention_fixture': 'fresh'}, 'values': [[fresh, 'retention-fresh']]}]})
        def logs(age):
            query = urlencode({'query': '{retention_fixture="' + age + '"}', 'start': str(int((now - (4 * 86400 if age == 'old' else 0) - 60) * 1e9)), 'end': str(int((now - (4 * 86400 if age == 'old' else 0) + 60) * 1e9))})
            return json.loads(curl(3100, '/loki/api/v1/query_range?' + query))['data']['result']
        until(lambda: bool(logs('old')) and bool(logs('fresh')), 'Loki aged and fresh fixtures are queryable before cleanup')
        curl(3100, '/flush', {})
        def aged_chunks():
            result = []
            for path in (fixture / 'data/loki/chunks/fake').glob('*/*'):
                start_ms = int(base64.b64decode(path.name).decode().split(':')[0], 16)
                if start_ms < (now - 3 * 86400) * 1000:
                    result.append(path)
            return result
        until(lambda: bool(aged_chunks()), 'Loki aged fixture has a physical chunk before deletion')
        old_chunks = aged_chunks()
        trace_id = '1234567890abcdef1234567890abcdef'
        curl(4418, '/v1/traces', {'resourceSpans': [{'resource': {'attributes': [{'key': 'service.name', 'value': {'stringValue': 'retention-fixture'}}]}, 'scopeSpans': [{'spans': [{'traceId': trace_id, 'spanId': '1234567890abcdef', 'name': 'retention-fixture', 'startTimeUnixNano': fresh, 'endTimeUnixNano': str(int((now + .01) * 1e9))}]}]}]})
        until(lambda: bool(list((fixture / 'data/tempo/blocks').glob('*/*/meta.json'))), 'Tempo fixture is sealed into backend storage')
        until(lambda: 'retention-fixture' in curl(3200, '/api/traces/' + trace_id), 'Tempo fixture is retrievable before aging')
        # Age the sealed fixture metadata, leaving the Parquet block valid. Only this isolated fixture is modified.
        metas = list((fixture / 'data/tempo/blocks').glob('*/*/meta.json'))
        for path in metas:
            meta = json.loads(path.read_text())
            aged = datetime.fromtimestamp(now - 4 * 86400, timezone.utc).isoformat().replace('+00:00', 'Z')
            meta['startTime'] = aged
            meta['endTime'] = aged
            path.write_text(json.dumps(meta))
        docker('restart', '-t', '5', NAME)
        until(lambda: bool(curl(3200, '/ready')), 'Tempo reloads the aged fixture metadata')
        until(lambda: all(not path.parent.exists() for path in metas), 'Tempo physically deletes aged trace blocks')
        fresh_trace = 'abcdef1234567890abcdef1234567890'
        stamp = int(time.time() * 1e9)
        curl(4418, '/v1/traces', {'resourceSpans': [{'scopeSpans': [{'spans': [{'traceId': fresh_trace, 'spanId': '1234567890abcdef', 'name': 'fresh-retention-control', 'startTimeUnixNano': str(stamp), 'endTimeUnixNano': str(stamp + 1000000)}]}]}]})
        def old_trace_status():
            return docker('exec', NAME, 'curl', '-s', '-o', '/dev/null', '-w', '%{http_code}', 'http://localhost:3200/api/traces/' + trace_id).strip()
        until(lambda: old_trace_status() == '404' and 'fresh-retention-control' in curl(3200, '/api/traces/' + fresh_trace), 'Tempo aged trace is unavailable and a fresh trace remains queryable')
        until(lambda: all(not path.exists() for path in old_chunks), 'Loki physically deletes aged fixture chunks', timeout=420)
        # Clear cached chunk/index readers after deletion; fresh data must survive this restart.
        docker('restart', '-t', '5', NAME)
        until(lambda: bool(curl(3100, '/ready')), 'Loki is ready after clearing stale query caches')
        until(lambda: not logs('old') and bool(logs('fresh')), 'Loki removes aged logs and preserves fresh logs')
        print('PASS: All three pinned backends enforce three-day retention on isolated aged fixtures', flush=True)
    finally:
        docker('rm', '-f', NAME)
        docker('run', '--rm', '-v', f'{folder}:/fixture:Z', '--entrypoint', 'chmod', IMAGE, '-R', 'a+rwX', '/fixture')
