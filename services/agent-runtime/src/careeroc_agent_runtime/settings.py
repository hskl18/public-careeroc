from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class AgentRuntimeSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="CAREEROC_AGENT_",
        case_sensitive=False,
        extra="ignore",
    )

    provider: str = "ollama"
    model: str = "gemma4:31b-cloud"
    base_url: str = "http://host.docker.internal:11434"
    api_key: str | None = None
    checkpoint_database_url: str | None = None
    shared_secret: str | None = None
    request_timeout_seconds: int = Field(default=90, ge=5, le=300)
    # A whole review must finish before the API stops waiting (AgentRuntime:TimeoutSeconds, 120 by
    # default); otherwise the API falls back while the graph keeps running and checkpoints a review
    # nobody will see.
    run_timeout_seconds: int = Field(default=100, ge=1, le=600)
    minimum_confidence: float = Field(default=0.65, ge=0, le=1)
    # A2A is served only when the runtime can reach the careeroc API to trade tokens for snapshots.
    api_base_url: str | None = None
    public_url: str = "http://localhost:8123"

    @property
    def model_path(self) -> str:
        return f"{self.provider.strip().lower()}/{self.model.strip()}"
