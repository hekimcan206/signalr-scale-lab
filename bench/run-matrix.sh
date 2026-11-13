#!/usr/bin/env bash
# Runs the benchmark matrix against the compose stack and writes one JSON file per run to benchmark-results/runs.
#
#   bench/run-matrix.sh                 # 3 repetitions, stages 1000,2500,5000,10000
#   REPS=1 STAGES=500,1000 bench/run-matrix.sh
#
# Every run starts from freshly restarted hub servers, proxy and Redis, so memory baselines are comparable. Scenarios
# are interleaved within a repetition (rep 1 of every scenario, then rep 2, ...), so a burst of load from something
# else on the host hits one repetition of several scenarios instead of every repetition of one.
set -euo pipefail

cd "$(dirname "$0")/.."
REPS="${REPS:-3}"
STAGES="${STAGES:-1000,2500,5000,10000}"
SCENARIOS="${SCENARIOS:-single sticky-only sticky-redis rr-negotiate rr-skip}"
OUT="benchmark-results/runs"
LOG="benchmark-results/host-load.log"
export LAB_HOST="${LAB_HOST:-unspecified}"
export MSYS_NO_PATHCONV=1

mkdir -p "$OUT"
docker compose build --quiet
docker compose up -d --wait redis hub-1 hub-2 hub-3 proxy

for rep in $(seq 1 "$REPS"); do
  for scenario in $SCENARIOS; do
    docker compose restart redis hub-1 hub-2 hub-3 proxy >/dev/null
    sleep 3
    {
      printf '%s %s rep %s: running containers on host %s, VM loadavg %s\n' \
        "$(date -u +%FT%TZ)" "$scenario" "$rep" \
        "$(docker ps -q | wc -l | tr -d ' ')" \
        "$(docker compose exec -T redis cat /proc/loadavg)"
    } | tee -a "$LOG"
    docker compose run --rm -T loadgen bench \
      --scenario "$scenario" --stages "$STAGES" --rep "$rep" \
      --out "/results/runs/${scenario}-${rep}.json"
  done
done

docker compose stop redis hub-1 hub-2 hub-3 proxy
