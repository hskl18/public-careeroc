from langchain_core.language_models.chat_models import BaseChatModel
from langchain_ollama import ChatOllama
from langchain_openai import ChatOpenAI

from careeroc_agent_runtime.settings import AgentRuntimeSettings


def build_chat_model(settings: AgentRuntimeSettings) -> BaseChatModel:
    provider = settings.provider.strip().lower()
    if provider == "ollama":
        client_kwargs: dict[str, object] = {}
        if settings.api_key:
            client_kwargs["headers"] = {"Authorization": f"Bearer {settings.api_key}"}
        return ChatOllama(
            model=settings.model,
            base_url=settings.base_url,
            client_kwargs=client_kwargs,
            temperature=0,
            validate_model_on_init=False,
        )

    if provider == "openai":
        return ChatOpenAI(
            model=settings.model,
            base_url=settings.base_url or None,
            api_key=settings.api_key,
            temperature=0,
            timeout=settings.request_timeout_seconds,
            max_retries=1,
        )

    raise ValueError(f"Unsupported CAREEROC_AGENT_PROVIDER: {settings.provider}")
