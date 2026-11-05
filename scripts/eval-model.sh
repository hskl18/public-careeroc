#!/usr/bin/env bash
# Scores the email pipeline against eval/pipeline-labels.json with a model behind its optional pass.
# Needs a signed-in local Ollama (`ollama signin`) and no API key. The thresholds were set for the
# rules alone, so a model can fail them; its scores are written to eval/models/ either way.
#
#   scripts/eval-model.sh gemma4:31b-cloud gpt-oss:120b-cloud
set -u
cd "$(dirname "$0")/.."
mkdir -p eval/models
for model in "$@"; do
  if Ollama__Enabled=true Ollama__BaseUrl="${OLLAMA_URL:-http://localhost:11434}" Ollama__Model="$model" \
    CAREEROC_EVAL_RESULTS_PATH="eval/models/${model//:/-}.json" \
    dotnet test tests/Careeroc.Tests/Careeroc.Tests.csproj \
    --filter "FullyQualifiedName~PipelineEvaluationTests" >/dev/null 2>&1; then
    echo "$model: met the thresholds"
  else
    echo "$model: below the thresholds"
  fi
done
