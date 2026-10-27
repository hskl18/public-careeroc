import { auth } from "@/auth";
import dynamic from "next/dynamic";
import { getAnalyticsWithFiltersForUser } from "@/lib/analytics-server";
import {
  normalizeAnalyticsProvider,
  normalizeAnalyticsRange,
} from "@/lib/analytics";

const AnalyticsWorkspace = dynamic(() =>
  import("@/components/analytics-workspace").then(
    (module) => module.AnalyticsWorkspace,
  ),
);

type AnalyticsPageProps = {
  searchParams?: Promise<Record<string, string | string[] | undefined>>;
};

export default async function AnalyticsPage({
  searchParams,
}: AnalyticsPageProps) {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const resolvedSearchParams = (await searchParams) ?? {};
  const selectedRange = normalizeAnalyticsRange(resolvedSearchParams.range);
  const selectedProvider = normalizeAnalyticsProvider(
    resolvedSearchParams.provider,
  );
  const { analytics, source } = await getAnalyticsWithFiltersForUser(
    {
      range: selectedRange,
      provider: selectedProvider,
    },
    user,
  );

  return (
    <AnalyticsWorkspace
      initialAnalytics={analytics}
      source={source}
      selectedRange={selectedRange}
      selectedProvider={selectedProvider}
    />
  );
}
