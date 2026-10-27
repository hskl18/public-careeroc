import type { MetadataRoute } from "next";

// Self-hosted instances hold personal job-search data, so nothing should be indexed.
export default function robots(): MetadataRoute.Robots {
  return { rules: [{ userAgent: "*", disallow: "/" }] };
}
