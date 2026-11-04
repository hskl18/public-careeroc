import type { Metadata } from "next";

export const siteName = "careeroc";

export const siteDescription =
  "careeroc is an open-source, multi-agent job search workspace that turns recruiting email into an evidence-backed pipeline of applications, deadlines, interviews, and next actions.";

export const repositoryUrl = "https://github.com/hskl18/public-careeroc";

export function buildPageMetadata({
  title,
  description = siteDescription,
}: {
  title: string;
  description?: string;
}): Metadata {
  return {
    title: title === siteName ? { absolute: siteName } : title,
    description,
  };
}
