#!/usr/bin/env bash
# Scores the email pipeline against eval/pipeline-labels.json with a model behind its optional pass.
# Needs a signed-in local Ollama (`ollama signin`) and no API key. The thresholds were set for the
# rules alone, so a model can fail them; its scores are written to eval/models/ either way.
#
#   scripts/eval-model.sh gemma4:31b-cloud gpt-oss:120b-cloud
set -euo pipefail
cd "$(dirname "$0")/.."
if [ "$#" -eq 0 ]; then
  echo "usage: scripts/eval-model.sh <ollama-model>..." >&2
  exit 2
fi
mkdir -p eval/models
for model in "$@"; do
  out="eval/models/${model//:/-}.json"
  # A stale file from an earlier run must not pass for this run's scores.
  rm -f "$out"
  if Ollama__Enabled=true Ollama__BaseUrl="${OLLAMA_URL:-http://localhost:11434}" Ollama__Model="$model" \
    CAREEROC_EVAL_RESULTS_PATH="$out" \
    dotnet test tests/Careeroc.Tests/Careeroc.Tests.csproj \
    --filter "FullyQualifiedName~PipelineEvaluationTests" >/dev/null 2>&1; then
    result="met the thresholds"
  else
    result="below the thresholds"
  fi
  # No results file means the run broke (build, restore, or Ollama), which is not a low score.
  if [ ! -s "$out" ]; then
    echo "$model: the eval did not run; rerun the dotnet test command above to see why" >&2
    exit 1
  fi
  echo "$model: $result"
done
