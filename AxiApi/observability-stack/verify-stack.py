#!/usr/bin/env python3
"""Read-only checks against the local stack after sending controlled AxiApi traffic."""
import json
from pathlib import Path
import re
import subprocess
import time
from urllib.parse import urlencode

ROOT = Path(__file__).resolve().parent
COMPOSE = ["docker", "compose", "-f", str(ROOT / "compose.yaml")]


def get(port, path):
    return subprocess.check_output(
        COMPOSE + ["exec", "-T", "lgtm", "curl", "--max-time", "15", "-fsS",
                   f"http://127.0.0.1:{port}{path}"], text=True, timeout=30)


def check(condition, description):
    if not condition:
        raise RuntimeError(description)
    print(f"PASS: {description}")


def main():
    dashboard = json.loads(get(3000, "/api/dashboards/uid/axiapi-observability"))
    check(dashboard["meta"]["provisioned"], "AxiApi dashboard is provisioned")
    sources = json.loads(get(3000, "/api/datasources"))
    sources = {source["uid"]: source for source in sources}
    check(all(uid in sources for uid in ("prometheus", "loki", "tempo")), "All three data sources exist")
    check(sources["tempo"]["jsonData"]["tracesToLogsV2"]["datasourceUid"] == "loki" and
          sources["loki"]["jsonData"]["derivedFields"][0]["datasourceUid"] == "tempo",
          "Trace/log navigation is provisioned in both directions")

    names = json.loads(get(9090, "/api/v1/label/__name__/values"))["data"]
    expected = ["axi_http_requests_total", "axi_http_request_duration_seconds_bucket",
                "axi_dependency_calls_total", "axi_dependency_duration_seconds_bucket",
                "axi_dependency_failures_total", "axi_database_retries_total", "axi_cache_outcomes_total"]
    check(all(name in names for name in expected), "All dashboard metrics have been ingested")
    for panel in dashboard["dashboard"]["panels"]:
        if panel["datasource"]["uid"] != "prometheus":
            continue
        query = panel["targets"][0]["expr"].replace("$__rate_interval", "1m").replace("$__range", "30m")
        result = json.loads(get(9090, "/api/v1/query?" + urlencode({"query": query})))
        check(result["status"] == "success", f"Dashboard query is valid: {panel['title']}")

    now = time.time()
    query = {"query": '{service_name="AxiApi"}', "start": str(int((now - 1800) * 1e9)),
             "end": str(int(now * 1e9)), "limit": "100"}
    logs = json.loads(get(3100, "/loki/api/v1/query_range?" + urlencode(query)))["data"]["result"]
    check(bool(logs), "AxiApi logs are queryable in Loki")
    check("PRIVATE_" not in json.dumps(logs), "Controlled private sentinel values are absent from stored logs")
    trace_ids = []
    for stream in logs:
        if stream["stream"].get("trace_id"):
            trace_ids.append(stream["stream"]["trace_id"])
        for value in stream["values"]:
            if len(value) > 2 and isinstance(value[2], dict) and value[2].get("trace_id"):
                trace_ids.append(value[2]["trace_id"])
    check(bool(trace_ids), "Stored logs contain trace correlation IDs")
    trace = json.loads(get(3200, "/api/traces/" + trace_ids[0]))
    check(bool(trace) and "PRIVATE_" not in json.dumps(trace), "A log's trace is retrievable in Tempo without private values")

    flags = json.loads(get(9090, "/api/v1/status/flags"))["data"]
    check(flags["storage.tsdb.retention.time"] == "3d", "Prometheus effective retention is three days")
    loki = get(3100, "/config")
    check(bool(re.search(r"retention_period:\s*(72h|3d)", loki)) and "retention_enabled: true" in loki,
          "Loki effective retention and deletion are configured")
    tempo = get(3200, "/status/config")
    check(bool(re.search(r"block_retention:\s*(72h|3d)", tempo)), "Tempo effective retention is three days")
    config = json.loads(subprocess.check_output(COMPOSE + ["config", "--format", "json"], text=True))
    service = config["services"]["lgtm"]
    check(all(port["host_ip"] == "127.0.0.1" for port in service["ports"]), "Published ports bind only to localhost")
    check(any(volume["type"] == "volume" and volume["target"] == "/data" for volume in service["volumes"]),
          "Backend data uses a persistent named volume")


if __name__ == "__main__":
    main()
