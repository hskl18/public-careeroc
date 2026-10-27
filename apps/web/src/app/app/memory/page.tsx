import Link from "next/link";
import { auth } from "@/auth";
import dynamic from "next/dynamic";
import { getMemoryReviewForUser } from "@/lib/profile-server";

const ProfileMemoryPanel = dynamic(() =>
  import("@/components/profile-memory-panel").then(
    (module) => module.ProfileMemoryPanel,
  ),
);

export default async function MemoryPage() {
  const session = await auth();
  const user = {
    email: session?.user?.email?.trim() || "local@careeroc.dev",
    name: session?.user?.name?.trim() || null,
  };
  const { profile, pendingMemory, source: profileSource, proxyPrefix } =
    await getMemoryReviewForUser(user);
  const displayName = profile.name || user.name || "careeroc user";

  return (
    <main className="app-scroll-main">
      <div className="mx-auto flex min-h-full w-full max-w-[104rem] flex-col gap-4 px-4 py-5 animate-in sm:px-5 sm:py-6">
        <section className="card app-workspace-panel shrink-0 p-4">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <p className="eyebrow">Memory</p>
              <h1 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
                Long-term memory review
              </h1>
              <p className="mt-2 max-w-2xl text-sm leading-7 text-[var(--text-secondary)]">
                Review facts and preferences agents can reuse across sessions.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Link href="/app/settings" className="btn btn-secondary btn-sm">
                Profile settings
              </Link>
              <Link href="/app/settings/data" className="btn btn-secondary btn-sm">
                Data controls
              </Link>
            </div>
          </div>
        </section>

        {profileSource === "fallback" ? (
          <div className="alert alert-warning">
            Live API data is unavailable right now. Memory review will reload when
            the API comes back.
          </div>
        ) : null}

        <ProfileMemoryPanel
          proxyPrefix={proxyPrefix}
          email={profile.email}
          displayName={displayName}
          initialMemory={profile.memory ?? []}
          initialPendingMemory={pendingMemory}
        />
      </div>
    </main>
  );
}
