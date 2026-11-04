import type { NextConfig } from "next";
import bundleAnalyzer from "@next/bundle-analyzer";

const nextConfig: NextConfig = {
  // Container images use the standalone server; Vercel builds ignore this setting.
  output: process.env.NEXT_OUTPUT === "standalone" ? "standalone" : undefined,
  devIndicators: false,
  reactCompiler: true,
  typedRoutes: true,
  async redirects() {
    // Config redirects resolve against the request host; route handlers built them from the
    // container's bind address (http://0.0.0.0:3000).
    return [
      { source: "/technology", destination: "/tech", permanent: true },
      { source: "/analytics", destination: "/app/analytics", permanent: true },
      { source: "/review", destination: "/app/review", permanent: true },
      { source: "/app/profile", destination: "/app/settings", permanent: true },
      // Routes of the retired sample demo; resumes and portfolios still link to /judge-demo.
      { source: "/judge-demo", destination: "/", permanent: true },
      { source: "/applications", destination: "/app/applications", permanent: true },
      { source: "/applications/:id", destination: "/app/applications", permanent: true },
      { source: "/resume", destination: "/app/resume", permanent: true },
      { source: "/settings", destination: "/app/settings", permanent: true },
      { source: "/notifications", destination: "/app", permanent: true },
      { source: "/agents", destination: "/tech", permanent: true },
    ];
  },
};

export default bundleAnalyzer({
  enabled: process.env.ANALYZE === "true",
})(nextConfig);
