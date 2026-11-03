import type { Metadata } from "next";
import { SpeedInsights } from "@vercel/speed-insights/next";
import Link from "next/link";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import "../styles/layout.css";
import "../styles/home.css";
import "../styles/components.css";
import "../styles/pages.css";
import "../styles/polish.css";
import "../styles/responsive.css";
import { AppNav } from "@/components/app-nav";
import { BrandLogo } from "@/components/brand-logo";
import { IconBell, IconReview, IconSettings, IconSpark } from "@/components/icons";
import type { ModelProviderStatus } from "@/lib/types";
import { readServerState } from "@/lib/server-state";

type HeaderTone = "ok" | "warn" | "danger" | "info" | "muted";

function modelHeaderState(
  status: ModelProviderStatus | "deterministic" | undefined
): { tone: HeaderTone; short: string; long: string } {
  switch (status) {
    case "ready":
      return { tone: "ok", short: "Gemma", long: "Gemma ready" };
    case "model_missing":
      return { tone: "warn", short: "Tag", long: "Model tag missing" };
    case "unavailable":
    case "health_check_failed":
      return { tone: "danger", short: "Cloud", long: "Ollama Cloud issue" };
    case "reachable":
      return { tone: "info", short: "Check", long: "Checking…" };
    default:
      return { tone: "muted", short: "Rules", long: "Deterministic" };
  }
}

const siteUrl = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";
const metadataTitle = "CareerOS - Recruiting Inbox Demo";
const metadataDescription =
  "A review-gated recruiting inbox workflow with deterministic fixtures, optional Gmail readonly sync, and an optional Gemma provider path.";

const geistSans = Geist({
  subsets: ["latin"],
  variable: "--font-geist",
  display: "swap"
});

const geistMono = Geist_Mono({
  subsets: ["latin"],
  variable: "--font-geist-mono",
  display: "swap"
});

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  applicationName: "CareerOS",
  title: {
    default: metadataTitle,
    template: "%s | CareerOS"
  },
  description: metadataDescription,
  keywords: [
    "CareerOS",
    "CareerOC",
    "Other Candidate",
    "Gemma 4",
    "Ollama",
    "Gmail",
    "job search",
    "multi-agent pipeline",
    "Kaggle Gemma 4 Good"
  ],
  authors: [{ name: "CareerOS" }],
  creator: "CareerOS",
  publisher: "CareerOS",
  category: "productivity",
  alternates: {
    canonical: "/"
  },
  openGraph: {
    type: "website",
    url: "/",
    siteName: "CareerOS",
    title: metadataTitle,
    description: metadataDescription
  },
  twitter: {
    card: "summary",
    title: metadataTitle,
    description: metadataDescription
  },
  robots: {
    index: true,
    follow: true
  },
  icons: {
    icon: [
      { url: "/favicon.ico", sizes: "any" },
      { url: "/icon.svg", type: "image/svg+xml" }
    ],
    apple: [{ url: "/apple-icon.png", sizes: "180x180", type: "image/png" }]
  }
};

export const dynamic = "force-dynamic";

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const state = await readServerState();
  const unreadCount = state.notifications.filter((item) => item.status === "unread").length;
  const criticalCount = state.notifications.filter(
    (item) => item.status !== "dismissed" && item.severity === "critical"
  ).length;
  const reviewCount = state.reviewItems.filter((item) => item.status === "open").length;
  const latestModelTrace = state.modelTraces.find((item) => item.provider === "ollama");
  const modelState = modelHeaderState(latestModelTrace?.status);
  const notificationsTone: HeaderTone = criticalCount > 0 ? "danger" : unreadCount > 0 ? "warn" : "muted";
  const notificationsLabel = unreadCount
    ? `${unreadCount} unread notification${unreadCount === 1 ? "" : "s"}`
    : "No unread notifications";
  const profileLabel = `Settings · signed in as ${state.workspaceUser.name}`;

  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable}`}>
      <body suppressHydrationWarning>
        <a className="skip-link" href="#main">
          Skip to content
        </a>
        <div className="app-shell">
          <header className="workspace-header">
            <div className="workspace-header-inner">
              <Link className="workspace-brand" href="/" prefetch={false}>
                <BrandLogo />
              </Link>

              <AppNav />

              <div className="workspace-header-spacer" />

              <div className="header-actions" role="group" aria-label="Workspace actions">
                <Link
                  className="workspace-mascot-link"
                  href="/agents"
                  prefetch={false}
                  aria-label="Open CareerOS agent contracts"
                  title="CareerOS agents"
                >
                  <img src="/mascots/inbox-buddy.svg" alt="" aria-hidden="true" />
                </Link>

                <div
                  className={`header-status-cluster header-tone-${modelState.tone}`}
                  aria-label={`Workspace pipeline · ${modelState.long}${reviewCount > 0 ? ` · ${reviewCount} review open` : ""}`}
                >
                  <Link
                    className="header-status-segment header-status-segment--model"
                    href="/settings"
                    prefetch={false}
                    aria-label={`Model mode: ${modelState.long}. Open settings.`}
                    title={modelState.long}
                  >
                    <span className="header-status-dot" aria-hidden="true" />
                    <IconSpark />
                    <span className="header-status-label">
                      <small>Workspace pipeline</small>
                      <strong>{modelState.long}</strong>
                    </span>
                    <span className="header-status-label-short" aria-hidden="true">{modelState.short}</span>
                  </Link>
                  {reviewCount > 0 ? (
                    <Link
                      className="header-status-segment header-status-segment--review"
                      href="/review"
                      prefetch={false}
                      aria-label={`${reviewCount} open review item${reviewCount === 1 ? "" : "s"}`}
                      title={`${reviewCount} open review`}
                    >
                      <IconReview />
                      <span className="header-status-label">
                        <small>Review</small>
                        <strong>{reviewCount}</strong>
                      </span>
                      <span className="header-status-label-short" aria-hidden="true">{reviewCount}</span>
                    </Link>
                  ) : null}
                </div>

                <Link
                  className={`header-icon-button header-tone-${notificationsTone}`}
                  href="/notifications"
                  prefetch={false}
                  aria-label={notificationsLabel}
                  title={notificationsLabel}
                >
                  <IconBell />
                  {unreadCount > 0 ? (
                    <span className="header-icon-button__count" aria-hidden="true">{unreadCount}</span>
                  ) : null}
                </Link>

                <Link
                  className="header-icon-button header-icon-button--ghost"
                  href="/settings"
                  prefetch={false}
                  aria-label={profileLabel}
                  title={profileLabel}
                >
                  <IconSettings />
                </Link>
              </div>
            </div>
          </header>

          <div className="workspace-body" id="main">
            {children}
          </div>

          <div className="workspace-bottom-nav">
            <AppNav mode="bottom" />
          </div>
        </div>
        <SpeedInsights />
      </body>
    </html>
  );
}
