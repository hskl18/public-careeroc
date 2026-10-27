"""OpenTelemetry for the agent runtime.

Tracing turns on only when OTEL_EXPORTER_OTLP_ENDPOINT is set, so tests and local runs
stay quiet. Agent spans use the OpenTelemetry GenAI semantic conventions, which are still
in Development status; the attribute names live here so a rename is a one-place change.
"""

from __future__ import annotations

import os

from fastapi import FastAPI
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

GEN_AI_OPERATION_NAME = "gen_ai.operation.name"
GEN_AI_AGENT_NAME = "gen_ai.agent.name"
GEN_AI_PROVIDER_NAME = "gen_ai.provider.name"
GEN_AI_REQUEST_MODEL = "gen_ai.request.model"
GEN_AI_CONVERSATION_ID = "gen_ai.conversation.id"

tracer = trace.get_tracer("careeroc.agent_runtime")


def configure_tracing(app: FastAPI) -> None:
    if not os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT"):
        return

    provider = TracerProvider(
        resource=Resource.create(
            {"service.name": os.getenv("OTEL_SERVICE_NAME", "careeroc-agent-runtime")}
        )
    )
    provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
    trace.set_tracer_provider(provider)
    FastAPIInstrumentor.instrument_app(app, tracer_provider=provider)
    HTTPXClientInstrumentor().instrument(tracer_provider=provider)


def agent_span(agent_name: str, model_path: str, thread_id: str):
    provider, _, model = model_path.partition("/")
    return tracer.start_as_current_span(
        f"invoke_agent {agent_name}",
        attributes={
            GEN_AI_OPERATION_NAME: "invoke_agent",
            GEN_AI_AGENT_NAME: agent_name,
            GEN_AI_PROVIDER_NAME: provider,
            GEN_AI_REQUEST_MODEL: model,
            GEN_AI_CONVERSATION_ID: thread_id,
        },
    )
