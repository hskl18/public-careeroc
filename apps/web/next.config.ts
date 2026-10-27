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
    ];
  },
};

export default bundleAnalyzer({
  enabled: process.env.ANALYZE === "true",
})(nextConfig);
