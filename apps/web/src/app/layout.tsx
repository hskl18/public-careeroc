import { SpeedInsights } from "@vercel/speed-insights/next";
import type { Metadata } from "next";
import { siteDescription, siteName } from "@/lib/site-metadata";
import "./globals.css";

export const metadata: Metadata = {
  applicationName: siteName,
  title: {
    default: siteName,
    template: `%s | ${siteName}`,
  },
  description: siteDescription,
  manifest: "/manifest.webmanifest",
  icons: {
    icon: [{ url: "/icon.svg" }],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className="h-full antialiased"
    >
      <body className="min-h-full flex flex-col">
        {children}
        {/* Its script is served only on Vercel; elsewhere it would 404 and fail e2e console checks. */}
        {process.env.VERCEL ? <SpeedInsights /> : null}
      </body>
    </html>
  );
}
