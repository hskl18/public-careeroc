// Tracing starts only when an OTLP endpoint is configured; spans continue into the API via traceparent.
export async function register() {
  if (!process.env.OTEL_EXPORTER_OTLP_ENDPOINT) {
    return;
  }

  const { registerOTel } = await import("@vercel/otel");
  const apiBaseUrl = process.env.API_BASE_URL;
  registerOTel({
    serviceName: process.env.OTEL_SERVICE_NAME ?? "careeroc-web",
    instrumentationConfig: {
      fetch: { propagateContextUrls: apiBaseUrl ? [apiBaseUrl] : [] },
    },
  });
}
