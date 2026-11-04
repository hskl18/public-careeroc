// Railway infrastructure for the public careeroc demo: Postgres, the .NET API, the LangGraph
// agent runtime, and the Next.js web app, each built from its own Dockerfile.
//
//   railway config plan     # preview
//   railway config apply    # create or update services and variables
//
// Secrets use preserve(): set them once with `railway variable set` (see docs/deploy/README.md)
// and every later apply keeps them. Cross-service values use Railway reference syntax,
// for example ${{api.CAREEROC_API_AUTH_SECRET}}, so each secret has one source.
import { defineRailway, postgres, preserve, project, service } from "railway/iac";

export default defineRailway(() => {
  const db = postgres("postgres");
  // A separate database for LangGraph checkpoints, so the runtime never holds careeroc's credentials.
  const checkpoints = postgres("agent-checkpoints");

  const agentRuntime = service("agent-runtime", {
    // Its Dockerfile copies from its own folder, so that folder is the build context.
    rootDirectory: "services/agent-runtime",
    build: { builder: "DOCKERFILE", dockerfilePath: "Dockerfile" },
    healthcheck: "/health/ready",
    healthcheckTimeout: 120,
    env: {
      PORT: "8123",
      CAREEROC_AGENT_PROVIDER: "deterministic",
      CAREEROC_AGENT_CHECKPOINT_DATABASE_URL: checkpoints.env.DATABASE_URL,
      CAREEROC_AGENT_SHARED_SECRET: preserve(),
      CAREEROC_AGENT_API_BASE_URL: "http://${{api.RAILWAY_PRIVATE_DOMAIN}}:8080",
      CAREEROC_AGENT_PUBLIC_URL: "https://${{RAILWAY_PUBLIC_DOMAIN}}",
    },
  });

  const api = service("api", {
    build: { builder: "DOCKERFILE", dockerfilePath: "apps/api/Dockerfile" },
    healthcheck: "/health/ready",
    healthcheckTimeout: 300,
    env: {
      PORT: "8080",
      ASPNETCORE_ENVIRONMENT: "Production",
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: "true",
      DATABASE_URL: db.env.DATABASE_URL,
      Database__ApplyMigrationsOnStartup: "true",
      CAREEROC_API_AUTH_SECRET: preserve(),
      CAREEROC_TOKEN_PROTECTION_KEY: preserve(),
      API_PUBLIC_BASE_URL: "https://${{RAILWAY_PUBLIC_DOMAIN}}",
      NEXT_PUBLIC_SITE_URL: "https://${{web.RAILWAY_PUBLIC_DOMAIN}}",
      ACCESS_ALLOWED_DOMAINS: "demo.careeroc.example",
      Demo__Enabled: "true",
      AgentRuntime__Enabled: "true",
      AgentRuntime__BaseUrl: "http://${{agent-runtime.RAILWAY_PRIVATE_DOMAIN}}:8123",
      AgentRuntime__SharedSecret: "${{agent-runtime.CAREEROC_AGENT_SHARED_SECRET}}",
      SyncWorker__Enabled: "false",
    },
  });

  const web = service("web", {
    build: { builder: "DOCKERFILE", dockerfilePath: "apps/web/Dockerfile" },
    healthcheck: "/",
    healthcheckTimeout: 120,
    env: {
      PORT: "3000",
      DEMO_MODE: "true",
      AUTH_SECRET: preserve(),
      AUTH_URL: "https://${{RAILWAY_PUBLIC_DOMAIN}}",
      NEXT_PUBLIC_SITE_URL: "https://${{RAILWAY_PUBLIC_DOMAIN}}",
      CAREEROC_API_AUTH_SECRET: "${{api.CAREEROC_API_AUTH_SECRET}}",
      API_BASE_URL: "http://${{api.RAILWAY_PRIVATE_DOMAIN}}:8080",
      API_PUBLIC_BASE_URL: "https://${{api.RAILWAY_PUBLIC_DOMAIN}}",
      AGENT_PUBLIC_BASE_URL: "https://${{agent-runtime.RAILWAY_PUBLIC_DOMAIN}}",
    },
  });

  return project("careeroc-demo", { resources: [db, checkpoints, agentRuntime, api, web] });
});
