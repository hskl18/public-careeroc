# ADR 004: Deterministic Fallback

Status: accepted

## Context

Provider credentials, model availability, latency, and output validity can fail independently of the recruiting workflow.
Anyone who clones the repository should be able to run the whole product, including the multi-agent review, without a paid key.

## Decision

The email pipeline runs a deterministic parser first; an optional model pass replaces its result only when the model is available, returns valid output, and is confident.
The agent runtime ships a deterministic suite that runs the same graph and evidence gate and labels its traces `deterministic/evidence-v1`.
When the runtime fails or times out, the API returns its own deterministic review with the status `completed_with_fallback`.
The pipeline eval scores the deterministic path against labeled demo email, and the evidence-gate eval runs with scripted model output, so neither needs a provider.

## Consequences

The core product loop, the demo, and CI run at zero provider cost and give the same answer every time.
Deterministic rules have limited language coverage, so ambiguous email goes to the review queue more often than a good model would send it.
A labeled eval of model-backed reviews is still future work.
